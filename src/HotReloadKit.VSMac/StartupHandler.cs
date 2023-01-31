
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
    using MonoDevelop.Components.Commands;
    using MonoDevelop.Ide;
    using MonoDevelop.Ide.TypeSystem;
    using MonoDevelop.Projects;
    using MonoDevelop.Core;
    using System.Net.Sockets;
    using HotReloadKit.Builder;
    using SlimMessenger;

    public class StartupHandler : CommandHandler
    {
        static int[] hotReloadServerPorts = new int[] { 5088, 5089, 50888, 50889 };

        // static memnbers

        static SemaphoreSlim listLockSemaphore = new SemaphoreSlim(1);
        static SemaphoreSlim changedFilesSemaphore = new SemaphoreSlim(0);
        static int asseblyVersion = 0;
        static DateTime beginTime;

        // private

        const string serverToken = "<<HotReloadKit>>";

        SlimServer hotReloadServer;

        DotNetProject memActiveProject = null;
        List<string> changedFilePaths = new List<string>();

        DotNetProject ActiveProject
            => (IdeApp.ProjectOperations.CurrentSelectedSolution?.StartupItem
                ?? IdeApp.ProjectOperations.CurrentSelectedBuildTarget)
                as DotNetProject;

        protected override void Run()
        {
            IdeServices.ProjectOperations.BeforeStartProject += ProjectOperations_BeforeStartProject;
        }

        void ProjectOperations_BeforeStartProject(object sender, EventArgs e)
        {
            beginTime = DateTime.Now;
            StartTcpServer();
            StartHotReloadSession();

            Task.Run(async () =>
            {
                await Task.Delay(1000);
                await IdeServices.ProjectOperations.CurrentRunOperation.Task;
                StopTcpServer();
            });
        }

        void StartTcpServer()
        {
            foreach(var port in hotReloadServerPorts)
            {
                try
                {
                    hotReloadServer = new SlimServer(port);

                    hotReloadServer.ServerStarted += server => Console.WriteLine($"Server started");
                    hotReloadServer.ServerStopped += server => Console.WriteLine($"Server stopped");
                    hotReloadServer.ClientConnected += Server_ClientConnected;
                    hotReloadServer.ClientDisconnected += client => Console.WriteLine($"Client disconnected: {client.Guid}");

                    hotReloadServer.Start();

                    Console.WriteLine($"HotReloadKit Server started, port: {port}");
                    break;
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.Message);
                    hotReloadServer?.Stop();
                }
            }
        }

        void StopTcpServer()
        {
            hotReloadServer?.Stop();
        }

        void Server_ClientConnected(SlimClient client)
        {
            Console.WriteLine($"Client connected: {client.Guid}");
            _ = ClientRunLoop(client);
        }

        async Task ClientRunLoop(SlimClient client)
        {
            await Task.Delay(500);
            await client.WriteAsync(serverToken);
            changedFilesSemaphore = new SemaphoreSlim(0);
            while (client.IsConnected)
            {
                try
                {
                    await changedFilesSemaphore.WaitAsync();

                    var assemblyName = GetAssemblyName();
                    var reloadToken = $@"<<|hotreload|{assemblyName}|hotreload|>>";

                    await client?.WriteAsync(reloadToken);

                    var message = await client.ReadAsync();
                    var hotReloadRequest = JsonSerializer.Deserialize<HotReloadRequest>(message);

                    await CompileAndEmitChanges(client, hotReloadRequest);
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.Message);
                }
            }
        }

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
            try
            {
                dynamic dynamic_target = IdeApp.Workspace.ActiveExecutionTarget;
                return dynamic_target.FrameworkShortName; // e.g. "net7.0-maccatalyst"
            }
            catch { }
            return null;
        }

        string GetAssemblyName()
        {
            var configuration = IdeApp.Workspace.ActiveConfiguration;
            return ActiveProject.GetOutputFileName(configuration).FileNameWithoutExtension;
        }

        string GetDllOutputhPath(string activeProjectName)
        {
            try
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
            catch { }
            return null;
        }

        async Task CompileAndEmitChanges(SlimClient client, HotReloadRequest hotReloadRequest)
        {
            try
            {
                asseblyVersion++;

                var dllName = GetAssemblyName();
                var dllOutputhPath = GetDllOutputhPath(dllName);
                var frameworkShortName = GetFrameworkShortName();

                // ------- compilation ---------

                var typeService = await Runtime.GetService<TypeSystemService>();
                var solution = typeService.Workspace.CurrentSolution;
                var projects = solution.Projects;
                var activeProject = solution.Projects.FirstOrDefault(e => e.AssemblyName.Equals(dllName) && (frameworkShortName == null || e.Name.Contains(frameworkShortName)));

                await listLockSemaphore.WaitAsync();
                if (changedFilePaths.Count() > 0)
                {
                    var codeCompilation = new CodeCompilation
                    {
                        HotReloadRequest = hotReloadRequest,
                        Solution = solution,
                        Project = activeProject,
                        DllOutputhPath = dllOutputhPath,
                        ChangedFilePaths = changedFilePaths.ToList()
                    };
                    changedFilePaths.Clear();
                    listLockSemaphore.Release();

                    await codeCompilation.Compile();

                    // ------- send data assembly ---------

                    await codeCompilation.EmitJsonDataAsync(async jsonData =>
                    {
                        await client.WriteAsync(jsonData);
                    });
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
        }

        const double timeSpanBetweenCompilations = 500;
        void ActiveProject_FileChangedInProject(object sender, ProjectFileEventArgs args)
        {
            _ = FileChanged(args);
        }

        async Task FileChanged(ProjectFileEventArgs args)
        {
            try
            {
                var lastChangedFiles =
                    args.Where(e => !e.ProjectFile.FilePath.FullPath.ToString().Contains(".g.cs"))
                        .Select(e => e.ProjectFile.FilePath.FullPath.ToString()).ToList();

                await Task.Delay(100);

                await listLockSemaphore.WaitAsync();
                var changed = false;
                foreach (var file in lastChangedFiles)
                    if (!changedFilePaths.Contains(file))
                    {
                        changed = true;
                        changedFilePaths.Add(file);
                    }
                listLockSemaphore.Release();

                if (changed) changedFilesSemaphore.Release();
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
            finally
            {
            }
        }
    }
}

#pragma warning restore CA1416
