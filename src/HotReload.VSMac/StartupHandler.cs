

#pragma warning disable CA1416

using System;
using System.Linq;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json;
using MonoDevelop.Components.Commands;
using MonoDevelop.Ide;
using MonoDevelop.Ide.TypeSystem;
using MonoDevelop.Projects;
using MonoDevelop.Core;
using HotReload.Builder;
using TcpServerSlim;


namespace HotReload
{
    public class StartupHandler : CommandHandler
    {
        static int asseblyVersion = 0;

        protected override void Run()
        {
            IdeServices.ProjectOperations.BeforeStartProject += ProjectOperations_BeforeStartProject;
        }

        DotNetProject ActiveProject
            => (IdeApp.ProjectOperations.CurrentSelectedSolution?.StartupItem
                ?? IdeApp.ProjectOperations.CurrentSelectedBuildTarget)
                as DotNetProject;

        void ProjectOperations_BeforeStartProject(object sender, EventArgs e)
        {
            StartTcpServer();
            StartHotReloadSession();
        }

        void StartTcpServer()
        {
            var server = new TcpServerSlim();

            server.ServerStarted += server => Console.WriteLine($"Server started");
            server.ServerStopped += server => Console.WriteLine($"Server stopped");
            server.ClientConnected += Server_ClientConnected;
            server.ClientDisconnected += client => Console.WriteLine($"Client disconnected: {client.Guid}");

            server.Run().Wait();
        }

        //        TcpClient hotReloadClient = null;
        //        CancellationTokenSource serverCancellationTokenSource;
        //        void StartTcpServer()
        //        {
        //            Task.Factory.StartNew(async () =>
        //            {
        //                var ipEndPoint = new IPEndPoint(IPAddress.Any, 9988);
        //                TcpListener listener = null;
        //                try
        //                {
        //                    listener = new TcpListener(ipEndPoint);
        //                    listener.Start();
        //                    Console.WriteLine("Server started");

        //                    serverCancellationTokenSource = new CancellationTokenSource();
        //                    hotReloadClient = await listener.AcceptTcpClientAsync(serverCancellationTokenSource.Token);
        //                    Console.WriteLine("Hot Reload connected");

        //                    var token = await hotReloadClient.GetStream().ReadStringAsync();
        //                    Console.WriteLine("Get Token");

        //                    StartHotReloadSession();

        //                    await IdeServices.ProjectOperations.CurrentRunOperation.Task;
        //                    Console.WriteLine("End running");

        //                    StopHotReloadSession();
        //                }
        //#pragma warning disable CS0168
        //                catch (Exception ex)
        //                {
        //                }
        //#pragma warning restore CS0168
        //                finally
        //                {
        //                    serverCancellationTokenSource?.Cancel();
        //                    hotReloadClient?.Close();
        //                    listener?.Stop();
        //                    hotReloadClient = null;
        //                }
        //            }, TaskCreationOptions.LongRunning);
        //        }

        DotNetProject memActiveProject = null;

        void StartHotReloadSession()
        {
            if (ActiveProject != null && memActiveProject == null)
            {
                memActiveProject = ActiveProject;
                memActiveProject.FileChangedInProject += ActiveProject_FileChangedInProject;
            }
        }

        void StopHotReloadSession()
        {
            if (memActiveProject != null)
            {
                memActiveProject.FileChangedInProject -= ActiveProject_FileChangedInProject;
                memActiveProject = null;
            }
        }

        string GetFrameworkShortName()
        {
            dynamic dynamic_target = IdeApp.Workspace.ActiveExecutionTarget;
            return dynamic_target.FrameworkShortName; // e.g. "net7.0-maccatalyst"
        }

        string GetDllOutputhPath(string activeProjectName)
        {
            var basePath = ActiveProject.MSBuildProject.BaseDirectory;

            var configuration = IdeApp.Workspace.ActiveConfiguration;
            var configurationName = configuration.ToString();

            dynamic dynamic_target = IdeApp.Workspace.ActiveExecutionTarget;
            string frameworkShortName = dynamic_target.FrameworkShortName; // e.g. "net7.0-maccatalyst"
            string runtimeIdentifier = dynamic_target.RuntimeIdentifier; // "maccatalyst-x64"

            var runtimeTail = !string.IsNullOrEmpty(runtimeIdentifier) ? $"/{runtimeIdentifier}" : "";

            return $"{basePath}/bin/{configurationName}/{frameworkShortName}{runtimeTail}/{activeProjectName}.dll";
        }

        async Task CompileAndEmitChanges(string activeProjectName, IEnumerable<string> changedFilePaths)
        {
            try
            {
                asseblyVersion++;

                var dllOutputhPath = GetDllOutputhPath(activeProjectName);
                var frameworkShortName = GetFrameworkShortName();

                using (NamedPipeClientStream pipeClient = new NamedPipeClientStream(".", activeProjectName, PipeDirection.InOut))
                {
                    // pipe connection
                    await pipeClient.ConnectAsync();

                    // ------- reequest types ---------

                    var streamReader = new StreamReader(pipeClient);
                    var jsonData = await streamReader.ReadLineAsync();
                    var hotReloadRequest = JsonSerializer.Deserialize<HotReloadRequest>(jsonData);

                    // ------- compilation ---------

                    var typeService = await Runtime.GetService<TypeSystemService>();
                    var solution = typeService.Workspace.CurrentSolution;
                    var projects = solution.Projects;
                    var activeProject = solution.Projects.FirstOrDefault(e => e.AssemblyName.Equals(activeProjectName) && e.Name.Contains(frameworkShortName));

                    var sharpCompilation = new SharpCompilation
                    {
                        HotReloadRequest = hotReloadRequest,
                        Solution = solution,
                        Project = activeProject,
                        DllOutputhPath = dllOutputhPath,
                        ChangedFilePaths = changedFilePaths
                    };

                    await sharpCompilation.Compile();

                    // ------- send data assembly ---------

                    var streamWriter = new StreamWriter(pipeClient);
                    streamWriter.AutoFlush = true;

                    await sharpCompilation.EmitJsonDataAsync(async jsonData =>
                    {
                        await streamWriter.WriteLineAsync(jsonData);
                    });
                }
            }
#pragma warning disable CS0168
            catch (Exception ex)
            {

            }
#pragma warning restore CS0168
        }

        static SemaphoreSlim semaphore = new SemaphoreSlim(1);
        async void ActiveProject_FileChangedInProject(object sender, ProjectFileEventArgs args)
        {
            await semaphore.WaitAsync();

            var configuration = IdeApp.Workspace.ActiveConfiguration;
            var dllName = ActiveProject.GetOutputFileName(configuration).FileNameWithoutExtension;

            var changedFilePaths = args
                .Where(e => !e.ProjectFile.FilePath.FullPath.ToString().Contains(".g.cs"))
                .Select(e => e.ProjectFile.FilePath.FullPath.ToString());

            await CompileAndEmitChanges(dllName, changedFilePaths);

            semaphore.Release();
        }
    }
}

#pragma warning restore CA1416
