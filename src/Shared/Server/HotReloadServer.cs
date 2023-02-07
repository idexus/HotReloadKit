using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using HotReloadKit.Builder;
using HotReloadKit.Shared;
using Microsoft.CodeAnalysis;
using SlimTcpServer;

namespace HotReloadKit.Server
{
    public class HotReloadServer
    {
        // startic

        readonly static int[] hotReloadServerPorts = new int[] { 5088, 5089, 5994, 5995, 5996, 5997, 5998 };
        readonly static int defaultConnectionResponseReadTimeout = 2000;

        static int asseblyVersion = 0;
        
        // private

        SlimServer hotReloadServer;
        SemaphoreSlim changedFilesSemaphoreTrig = new SemaphoreSlim(0);
        readonly List<string> changedFilePaths = new List<string>();
        CancellationTokenSource cancellationTokenSource;

        // public

        public Solution Solution { get; set; }
        public Project ActiveProject { get; set; }
        public HotReloadClientConnectionData ConnectionData { get; private set; }

        public event Action HotReloadStarted;
        public event Action HotReloadStopped;

        public async Task StartServerAsync()
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
                    hotReloadServer.ClientDisconnected += client => Debug.WriteLine($"HotReloadKit client disconnected guid: {client.Guid}");

                    await hotReloadServer.StartAsync(port);

                    Debug.WriteLine($"HotReloadKit tcp port: {port}");
                    break;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine(ex.Message);
                    await hotReloadServer?.StopAsync();
                }
            }
        }

        Task hotReloadClientTask;
        void Server_ClientConnected(SlimClient client)
        {
            Debug.WriteLine($"HotReloadKit client connected guid: {client.Guid}");
            ClientRunLoop(client);
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
            await hotReloadServer?.StopAsync();
            if (hotReloadClientTask != null) await hotReloadClientTask;
        }

        void ClientRunLoop(SlimClient client)
        {
            hotReloadClientTask = Task.Run(async () =>
            {
                try
                {
                    changedFilesSemaphoreTrig = new SemaphoreSlim(0);

                    // clear list
                    lock (changedFilePaths)
                    {
                        changedFilePaths.Clear();
                    }

                    // send server token
                    var serverConnectionData = new HotReloadServerConnectionData
                    {
                        Token = HotReloadServerConnectionData.DefaultToken,
                        Version = HotReloadServerConnectionData.CurrentVersion,
                        Guid = client.Guid
                    };
                    await client.WriteAsync(JsonSerializer.Serialize(serverConnectionData));

                    var connectionMessage = await client.ReadAsync(defaultConnectionResponseReadTimeout);
                    ConnectionData = JsonSerializer.Deserialize<HotReloadClientConnectionData>(connectionMessage);
                    if (ConnectionData != null && ConnectionData.Type == nameof(HotReloadClientConnectionData))
                    {
                        Debug.WriteLine($"HotReloadKit session assembly: {ConnectionData.AssemblyName} platform: {ConnectionData.PlatformName}");

                        HotReloadStarted();

                        // client loop
                        while (client.IsConnected && !cancellationTokenSource.Token.IsCancellationRequested)
                        {
                            try
                            {
                                await changedFilesSemaphoreTrig.WaitAsync(cancellationTokenSource.Token);

                                await client?.WriteAsync(JsonSerializer.Serialize(new HotReloadRequest()));

                                var message = await client.ReadAsync();
                                var additionaTypesMessage = JsonSerializer.Deserialize<HotReloadRequestAdditionalTypesMessage>(message);

                                await CompileAndEmitChangesAsync(client, additionaTypesMessage.TypeNames);
                            }
                            catch (Exception ex)
                            {
                                Debug.WriteLine(ex.Message);
                            }
                        }

                        HotReloadStopped();
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine(ex);
                }
            });
        }

        async Task CompileAndEmitChangesAsync(SlimClient client, string[] additionaTypeNames)
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
                            AdditionalTypeNames = additionaTypeNames,
                            Solution = Solution,
                            Project = ActiveProject,
                            ChangedFilePaths = changedFilePaths.ToList()
                        };
                    }
                }

                if (codeCompilation != null)
                {
                    await codeCompilation.CompileAsync();

                    // ------- send data assembly ---------
                    await codeCompilation.EmitDataAsync(async (string[] typeNames, byte[] dllData, byte[] pdbData) =>
                    {
                        var hotReloadData = new HotReloadData
                        {
                            TypeNames = typeNames,
                            DllData = dllData,
                            PdbData = pdbData
                        };
                        await client.WriteAsync(JsonSerializer.Serialize(hotReloadData));
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

