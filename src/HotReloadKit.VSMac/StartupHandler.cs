
#pragma warning disable CA1416


namespace HotReloadKit.VSMac
{
    using System;
    using System.Linq;
    using System.Collections.Generic;
    using System.IO;
    using System.IO.Pipes;
    using System.Threading;
    using System.Threading.Tasks;
    using System.Text.Json;
    using System.Diagnostics;
    using MonoDevelop.Components.Commands;
    using MonoDevelop.Ide;
    using MonoDevelop.Ide.TypeSystem;
    using MonoDevelop.Projects;
    using MonoDevelop.Core;
    using System.Net.Sockets;
    using HotReloadKit.Server;
     
    public class StartupHandler : CommandHandler
    {
        // private

        readonly Dictionary<string, DateTime> modificationDateTimeDict = new Dictionary<string, DateTime>();

        DotNetProject activeProject
            => (IdeApp.ProjectOperations.CurrentSelectedSolution?.StartupItem ??
                IdeApp.ProjectOperations.CurrentSelectedBuildTarget) as DotNetProject;

        DotNetProject memActiveProject = null;
        HotReloadServer hotReloadServer;

        protected override void Run()
        {
            IdeServices.ProjectOperations.BeforeStartProject += ProjectOperations_BeforeStartProject;
        }

        void ProjectOperations_BeforeStartProject(object sender, EventArgs e)
        {
            _ = RunHotReloadServer();
        }

        SemaphoreSlim sessionSemaphore = new SemaphoreSlim(1);
        async Task RunHotReloadServer()
        {
            await sessionSemaphore.WaitAsync();

            hotReloadServer = new HotReloadServer();
            hotReloadServer.HotReloadStarted += StartHotReloadSession;
            hotReloadServer.HotReloadStopped += StopHotReloadSession;

            await hotReloadServer.StartServerAsync();

            await Task.Delay(1000);
            await IdeServices.ProjectOperations.CurrentRunOperation.Task;
            await hotReloadServer.StopServerAsync();

            sessionSemaphore.Release();
        }

        void StartHotReloadSession()
        {
            _ = Task.Run(async () =>
            {
                if (activeProject != null && memActiveProject == null)
                {
                    var platformName = hotReloadServer.ConnectionData.PlatformName;
                    var assemblyName = hotReloadServer.ConnectionData.AssemblyName;

                    var typeService = await Runtime.GetService<TypeSystemService>();
                    hotReloadServer.Solution = typeService.Workspace.CurrentSolution;
                    hotReloadServer.ActiveProject = typeService.Workspace.CurrentSolution.Projects
                        .FirstOrDefault(e => e.AssemblyName.Equals(assemblyName) && (platformName == null || e.Name.Contains(platformName)));

                    memActiveProject = activeProject;
                    memActiveProject.FileChangedInProject += ActiveProject_FileChangedInProject;
                    Debug.WriteLine($"HotReloadKit session started");
                }
            });
        }

        void StopHotReloadSession()
        {
            if (memActiveProject != null)
            {
                memActiveProject.FileChangedInProject -= ActiveProject_FileChangedInProject;
                memActiveProject = null;
                Debug.WriteLine($"HotReloadKit session stopped");
            }
        }

        void ActiveProject_FileChangedInProject(object sender, ProjectFileEventArgs args)
        {
            try
            {
                var lastChangedFiles =
                    args.Where(e => !e.ProjectFile.FilePath.FullPath.ToString().Contains(".g.cs"))
                        .Select(e => e.ProjectFile.FilePath.FullPath.ToString()).ToList();

                var changed = false;
                foreach (var file in lastChangedFiles)
                {
                    modificationDateTimeDict.TryGetValue(file, out var lastDateTime);
                    var actualDateTime = File.GetLastWriteTime(file);

                    Debug.WriteLine($"HotReloadKit --FILE CHANGED-- {file} last: {lastDateTime} current: {actualDateTime}");

                    if (lastDateTime != actualDateTime)
                    {
                        changed = true;
                        hotReloadServer?.AddChangedFile(file);
                        modificationDateTimeDict[file] = actualDateTime;
                    }
                }
                if (changed) hotReloadServer?.TrigChangedFiles();
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.Message);
            }
        }
    }
}

#pragma warning restore CA1416
