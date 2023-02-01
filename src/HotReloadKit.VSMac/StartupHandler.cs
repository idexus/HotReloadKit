
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
        static int[] hotReloadServerPorts = new int[] { 50888, 50889, 5088, 5089, 50888 };

        // static memnbers

        static SemaphoreSlim lockSemaphore = new SemaphoreSlim(1);
        static SemaphoreSlim changedFilesSemaphore = new SemaphoreSlim(0);
        static int asseblyVersion = 0;

        // private

        const string serverToken = "<<HotReloadKit>>";
        readonly List<string> changedFilePaths = new List<string>();
        readonly Dictionary<string, DateTime> modificationDateTimeDict = new Dictionary<string, DateTime>();

        SlimServer hotReloadServer;

        DotNetProject activeProject
            => (IdeApp.ProjectOperations.CurrentSelectedSolution?.StartupItem ??
                IdeApp.ProjectOperations.CurrentSelectedBuildTarget) as DotNetProject;

        DotNetProject memActiveProject = null;

        protected override void Run()
        {
            IdeServices.ProjectOperations.BeforeStartProject += ProjectOperations_BeforeStartProject;
        }

        void ProjectOperations_BeforeStartProject(object sender, EventArgs e)
        {            
            StartTcpServer();

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

                    hotReloadServer.ServerStarted += server => Console.WriteLine($"HotReloadKit server started");
                    hotReloadServer.ServerStopped += server => Console.WriteLine($"HotReloadKit server stopped");
                    hotReloadServer.ClientConnected += Server_ClientConnected;
                    hotReloadServer.ClientDisconnected += client => Console.WriteLine($"HotReloadKit client disconnected: {client.Guid}");

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
            Console.WriteLine($"HotReloadKit client connected: {client.Guid}");
            _ = ClientRunLoop(client);
        }

        async Task ClientRunLoop(SlimClient client)
        {
            changedFilesSemaphore = new SemaphoreSlim(0);

            // clear list
            await lockSemaphore.WaitAsync();
            modificationDateTimeDict.Clear();
            changedFilePaths.Clear();
            lockSemaphore.Release();

            // send server token
            await Task.Delay(1000);
            await client.WriteAsync(serverToken);

            StartHotReloadSession();

            // client loop
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

            StopHotReloadSession();
        }

        void StartHotReloadSession()
        {
            if (activeProject != null && memActiveProject == null)
            {
                memActiveProject = activeProject;
                memActiveProject.FileChangedInProject += ActiveProject_FileChangedInProject;
                Console.WriteLine($"HotReloadKit session started");
            }
        }

        void StopHotReloadSession()
        {
            if (memActiveProject != null)
            {
                memActiveProject.FileChangedInProject -= ActiveProject_FileChangedInProject;
                memActiveProject = null;
                Console.WriteLine($"HotReloadKit session stopped");
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
            return activeProject.GetOutputFileName(configuration).FileNameWithoutExtension;
        }

        string GetDllOutputhPath(string activeProjectName)
        {
            try
            {
                var basePath = activeProject.MSBuildProject.BaseDirectory;

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

                await lockSemaphore.WaitAsync();
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
                    lockSemaphore.Release();

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

                await lockSemaphore.WaitAsync();
                foreach (var file in lastChangedFiles)
                {
                    modificationDateTimeDict.TryGetValue(file, out var lastDateTime);
                    var actualDateTime = File.GetLastWriteTime(file);

                    Console.WriteLine($"HotReloadKit --FILE CHANGED-- {file} last: {lastDateTime} current: {actualDateTime}");

                    if (!changedFilePaths.Contains(file) && lastDateTime != actualDateTime)
                    {
                        changedFilePaths.Add(file);
                        modificationDateTimeDict[file] = actualDateTime;
                    }
                }
                if (changedFilePaths.Count() > 0) changedFilesSemaphore.Release();
                lockSemaphore.Release();
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
        }
    }
}

#pragma warning restore CA1416
