

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
using TcpServer.Slim;


namespace CodeReloader.VSMac
{
    public class StartupHandler : CommandHandler
    {
        // static memnbers

        static SemaphoreSlim semaphore = new SemaphoreSlim(1);
        static int asseblyVersion = 0;

        // private

        TcpServerSlim tcpServer;
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
            StartTcpServer();
            StartHotReloadSession();
        }

        void StartTcpServer()
        {

            tcpServer = new TcpServerSlim();

            tcpServer.ServerStarted += server => Console.WriteLine($"Server started");
            tcpServer.ServerStopped += server => Console.WriteLine($"Server stopped");
            tcpServer.ClientConnected += Server_ClientConnected;
            tcpServer.ClientDisconnected += client => Console.WriteLine($"Client disconnected: {client.Guid}");

            tcpServer.Start();
        }

        private void Server_ClientConnected(TcpClientSlim client)
        {
            client.DataReceived += Client_DataReceived; ;
            Console.WriteLine($"Client connected: {client.Guid}");
        }

        private void Client_DataReceived(TcpClientSlim client, string message)
        {
            Console.WriteLine($@"Client: {client.Guid}, data received");

            Task.Run(async () =>
            {
                await semaphore.WaitAsync();

                var configuration = IdeApp.Workspace.ActiveConfiguration;
                var dllName = ActiveProject.GetOutputFileName(configuration).FileNameWithoutExtension;

                await CompileAndEmitChanges(dllName, changedFilePaths);
                changedFilePaths.Clear();

                semaphore.Release();
            });
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

                    var codeCompilation = new CodeCompilation
                    {
                        HotReloadRequest = hotReloadRequest,
                        Solution = solution,
                        Project = activeProject,
                        DllOutputhPath = dllOutputhPath,
                        ChangedFilePaths = changedFilePaths
                    };

                    await codeCompilation.Compile();

                    // ------- send data assembly ---------

                    var streamWriter = new StreamWriter(pipeClient);
                    streamWriter.AutoFlush = true;

                    await codeCompilation.EmitJsonDataAsync(async jsonData =>
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

        async void ActiveProject_FileChangedInProject(object sender, ProjectFileEventArgs args)
        {
            await semaphore.WaitAsync();

            var lastChangedFiles =
                args.Where(e => !e.ProjectFile.FilePath.FullPath.ToString().Contains(".g.cs"))
                    .Select(e => e.ProjectFile.FilePath.FullPath.ToString());

            changedFilePaths.AddRange(lastChangedFiles);

            semaphore.Release();
        }
    }
}

#pragma warning restore CA1416
