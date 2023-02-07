using EnvDTE;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.OLE.Interop;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Events;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.Win32;
using System;
using System.Linq;
using System.ComponentModel.Design;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using SolutionEvents = EnvDTE.SolutionEvents;
using Task = System.Threading.Tasks.Task;
using Newtonsoft.Json;
using HotReloadKit.Server;
using Microsoft.VisualStudio.ComponentModelHost;
using System.Reflection;
using Microsoft.CodeAnalysis;
using VSLangProj;
using Microsoft.VisualStudio.VCProject;
using System.Windows.Interop;
using System.Collections.Generic;

namespace HotReloadKit.VS
{
    /// <summary>
    /// This is the class that implements the package exposed by this assembly.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The minimum requirement for a class to be considered a valid package for Visual Studio
    /// is to implement the IVsPackage interface and register itself with the shell.
    /// This package uses the helper classes defined inside the Managed Package Framework (MPF)
    /// to do it: it derives from the Package class that provides the implementation of the
    /// IVsPackage interface and uses the registration attributes defined in the framework to
    /// register itself and its components with the shell. These attributes tell the pkgdef creation
    /// utility what data to put into .pkgdef file.
    /// </para>
    /// <para>
    /// To get loaded into VS, the package must be referred by &lt;Asset Type="Microsoft.VisualStudio.VsPackage" ...&gt; in .vsixmanifest file.
    /// </para>
    /// </remarks>
    /// 
    [ProvideAutoLoad(UIContextGuids80.NoSolution, PackageAutoLoadFlags.BackgroundLoad)]
    [ProvideAutoLoad(UIContextGuids80.SolutionExists, PackageAutoLoadFlags.BackgroundLoad)]
    [PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
    [Guid(HotReloadKitPackage.PackageGuidString)]
    [SuppressMessage("StyleCop.CSharp.DocumentationRules", "SA1650:ElementDocumentationMustBeSpelledCorrectly", Justification = "pkgdef, VS and vsixmanifest are valid VS terms")]
    public sealed class HotReloadKitPackage : AsyncPackage
    {
        /// <summary>
        /// HotReloadKitPackage GUID string.
        /// </summary>
        public const string PackageGuidString = "3c467327-b102-43a0-bf38-ecdc25e73638";

        /// <summary>
        /// Initializes a new instance of the <see cref="HotReloadKitPackage"/> class.
        /// </summary>
        public HotReloadKitPackage()
        {
            // Inside this method you can place any initialization code that does not require
            // any Visual Studio service because at this point the package object is created but
            // not sited yet inside Visual Studio environment. The place to do all the other
            // initialization is the Initialize method.
        }

        #region Package Members

        DTE dte;

        DocumentEvents documentEvents;
        DebuggerEvents debuggerEvents;

        /// <summary>
        /// Initialization of the package; this method is called right after the package is sited, so this is the place
        /// where you can put all the initialization code that rely on services provided by VisualStudio.
        /// </summary>
        /// <param name="cancellationToken">A cancellation token to monitor for initialization cancellation, which can occur when VS is shutting down.</param>
        /// <param name="progress">A provider for progress updates.</param>
        /// <returns>A task representing the async work of package initialization, or an already completed task if there is none. Do not return null from this method.</returns>
        protected override async Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
        {
            // When initialized asynchronously, the current thread may be a background thread at this point.
            // Do any initialization that requires the UI thread after switching to the UI thread.
            await this.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

            dte = (DTE)(await GetServiceAsync(typeof(DTE)));

            documentEvents = dte.Events.DocumentEvents;
            debuggerEvents = dte.Events.DebuggerEvents;

            debuggerEvents.OnEnterRunMode += DebuggerEvents_OnEnterRunMode;
            debuggerEvents.OnEnterBreakMode += DebuggerEvents_OnEnterBreakMode;
            debuggerEvents.OnEnterDesignMode += DebuggerEvents_OnEnterDesignMode;
        }


        bool hotReloadPaused = false;
        bool hotReloadRunning = false;
        HotReloadServer hotReloadServer;
        CancellationTokenSource sessionCancellationTokenSource;

        SemaphoreSlim sessionSemaphore = new SemaphoreSlim(1);
        async Task RunHotReloadServerAsync()
        {
            await sessionSemaphore.WaitAsync();
            hotReloadRunning = true;
            try
            {
                Debug.WriteLine($"---------------- HotReloadKit - BEGIN ----------------");

                sessionCancellationTokenSource = new CancellationTokenSource();

                hotReloadServer = new HotReloadServer();
                hotReloadServer.HotReloadStarted += HotReloadServer_HotReloadStarted;
                hotReloadServer.HotReloadStopped += HotReloadServer_HotReloadStopped;

                await hotReloadServer.StartServerAsync();

                await Task.Delay(1000);
                try
                {
                    await Task.Delay(Timeout.Infinite, sessionCancellationTokenSource.Token);
                }
                catch { }
                
                await hotReloadServer.StopServerAsync();

                Debug.WriteLine($"---------------- HotReloadKit - END ----------------");
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex);
            }
            hotReloadRunning = false;
            sessionSemaphore.Release();
        }

        void StopHotReload()
        {
            sessionCancellationTokenSource?.Cancel();            
        }

        private void HotReloadServer_HotReloadStarted()
        {
            _ = Task.Run(async () =>
            {
                await this.JoinableTaskFactory.SwitchToMainThreadAsync();

                var componentModel = (IComponentModel)Microsoft.VisualStudio.Shell.Package.GetGlobalService(typeof(SComponentModel));
                var workspace = componentModel.GetService<Microsoft.VisualStudio.LanguageServices.VisualStudioWorkspace>();

                var platformName = hotReloadServer.ConnectionData.PlatformName;
                var assemblyName = hotReloadServer.ConnectionData.AssemblyName;

                hotReloadServer.Solution = workspace.CurrentSolution;
                hotReloadServer.ActiveProject = workspace
                    .CurrentSolution.Projects.FirstOrDefault(e => e.AssemblyName.Equals(assemblyName) && (platformName == null || e.OutputFilePath.Contains(platformName)));

                documentEvents.DocumentSaved += DocumentEvents_DocumentSaved;
                Debug.WriteLine($"HotReloadKit session started");
            });
        }

        private void HotReloadServer_HotReloadStopped()
        {
            _ = Task.Run(async () =>
            {
                await this.JoinableTaskFactory.SwitchToMainThreadAsync();
                documentEvents.DocumentSaved -= DocumentEvents_DocumentSaved;
                Debug.WriteLine($"HotReloadKit session stopped");
            });
        }



        private void DebuggerEvents_OnEnterDesignMode(dbgEventReason Reason)
        {
            StopHotReload();
        }

        private void DebuggerEvents_OnEnterBreakMode(dbgEventReason Reason, ref dbgExecutionAction ExecutionAction)
        {
            hotReloadPaused = true;
        }

        private void DebuggerEvents_OnEnterRunMode(dbgEventReason Reason)
        {
            hotReloadPaused = false;
            if (!hotReloadRunning)
            {
                _ = RunHotReloadServerAsync();
            }
        }

        private void DocumentEvents_DocumentSaved(EnvDTE.Document Document)
        {
            _ = Task.Run(async () =>
            {
                if (!hotReloadPaused)
                {
                    await this.JoinableTaskFactory.SwitchToMainThreadAsync();
                    var fileName = Document.FullName;
                    Debug.WriteLine($"HotReloadKit --FILE CHANGED-- {fileName}");
                    hotReloadServer.AddChangedFile(fileName);
                    hotReloadServer.TrigChangedFiles();
                }
            });
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                StopHotReload();
                System.Diagnostics.Trace.Flush();
            }

            base.Dispose(disposing);
        }

        #endregion
    }
}
