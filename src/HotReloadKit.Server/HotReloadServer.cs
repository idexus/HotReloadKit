using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using HotReloadKit.Builder;
using Microsoft.CodeAnalysis;
using SlimTcpServer;

namespace HotReloadKit.Server
{
    public class HotReloadServer
    {
        // startic

        static int asseblyVersion = 0;
        static int[] hotReloadServerPorts = new int[] { 50888, 50889, 5088, 5089, 60088, 60888 };

        // private

        SlimServer hotReloadServer;
        SemaphoreSlim changedFilesSemaphoreTrig = new SemaphoreSlim(0);
        readonly List<string> changedFilePaths = new List<string>();
        readonly Solution solution;
        readonly Project activeProject;
        readonly string assemblyName;
        readonly string assemblyOutputhPath;
        CancellationTokenSource cancellationTokenSource;

        // public

        public event Action HotReloadStarted;
        public event Action HotReloadStopped;

        public HotReloadServer(Solution solution, Project activeProject, string assemblyName, string assemblyOutputhPath)
        {
            this.solution = solution;
            this.activeProject = activeProject;
            this.assemblyName = assemblyName;
            this.assemblyOutputhPath = assemblyOutputhPath;
        }


        public async Task StartServer()
        {
            cancellationTokenSource = new CancellationTokenSource();
            foreach (var port in hotReloadServerPorts)
            {
                try
                {
                    hotReloadServer = new SlimServer();

                    hotReloadServer.ServerStarted += server => Debug.WriteLine($"HotReloadKit server started");
                    hotReloadServer.ServerStopped += server => Debug.WriteLine($"HotReloadKit server stopped");
                    hotReloadServer.ClientConnected += Server_ClientConnected;
                    hotReloadServer.ClientDisconnected += client => Debug.WriteLine($"HotReloadKit client disconnected: {client.Guid}");

                    await hotReloadServer.Start(port);

                    Debug.WriteLine($"HotReloadKit tcp port: {port}");
                    break;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine(ex.Message);
                    await hotReloadServer?.Stop();
                }
            }
        }

        void Server_ClientConnected(SlimClient client)
        {
            Debug.WriteLine($"HotReloadKit client connected: {client.Guid}");
            _ = ClientRunLoop(client);
        }

        public void AddChangedFile(string fileName)
        {
            Task.Run(() =>
            {
                lock(changedFilePaths)
                {
                    changedFilePaths.Add(fileName);
                }
            });         
        }

        public void TrigChangedFiles()
        {
            if (changedFilesSemaphoreTrig.CurrentCount == 0)
                changedFilesSemaphoreTrig.Release();
        }

        public async Task StopServer()
        {
            cancellationTokenSource?.Cancel();
            await hotReloadServer?.Stop();
        }

        async Task ClientRunLoop(SlimClient client)
        {
            changedFilesSemaphoreTrig = new SemaphoreSlim(0);

            // clear list
            lock (changedFilePaths)
            {
                changedFilePaths.Clear();
            }

            // send server token
            var serverToken = $@"<<|HotReloadKit|{assemblyName}|connect|>>";
            await client.WriteAsync(serverToken);

            HotReloadStarted();

            // client loop
            while (client.IsConnected && !cancellationTokenSource.Token.IsCancellationRequested)
            {
                try
                {
                    await changedFilesSemaphoreTrig.WaitAsync(cancellationTokenSource.Token);

                    var reloadToken = $@"<<|HotReloadKit|{assemblyName}|hotreload|>>";

                    await client?.WriteAsync(reloadToken);

                    var message = await client.ReadAsync();
                    var hotReloadRequest = JsonSerializer.Deserialize<HotReloadRequest>(message);

                    await CompileAndEmitChanges(client, hotReloadRequest);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine(ex.Message);
                }
            }

            HotReloadStopped();
        }

        async Task CompileAndEmitChanges(SlimClient client, HotReloadRequest hotReloadRequest)
        {
            try
            {
                asseblyVersion++;



                // ------- compilation ---------

                
                CodeCompilation codeCompilation = null;

                lock (changedFilePaths)
                {
                    if (changedFilePaths.Count > 0)
                    {
                        codeCompilation = new CodeCompilation
                        {
                            HotReloadRequest = hotReloadRequest,
                            Solution = solution,
                            Project = activeProject,
                            DllOutputhPath = assemblyOutputhPath,
                            ChangedFilePaths = changedFilePaths.ToList()
                        };
                    }
                }

                if (codeCompilation != null)
                {
                    await codeCompilation.Compile();

                    // ------- send data assembly ---------

                    await codeCompilation.EmitJsonDataAsync(async hotReloadData =>
                    {
                        var jsonData = JsonSerializer.Serialize(hotReloadData);
                        await client.WriteAsync(jsonData);
                    });

                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.Message);
            }
        }
    }
}

