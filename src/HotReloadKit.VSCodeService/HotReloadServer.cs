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
        readonly int[] hotReloadServerPorts = new int[] { 5088, 5089, 5994, 5995, 5996, 5997, 5998 };
        readonly static int defaultConnectionResponseReadTimeout = 2000;

        static Dictionary<string, DebugContext> activeContexts = new Dictionary<string, DebugContext>();

        SlimServer? hotReloadServer;
        CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();

        public void RegisterProject(ProjectInfo projectInfo)
        {
            lock (activeContexts)
            {
                activeContexts[projectInfo.DebugInfo.ProjectPath] = new DebugContext { ProjectInfo = projectInfo };
            }
        }

        public void UnregisterProject(string projectPath)
        {
            lock (activeContexts)
            {
                activeContexts.Remove(projectPath);
            }
        }

        public async Task StartServerAsync()
        {
            if (hotReloadServer == null)
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
                        hotReloadServer.ClientDisconnected += Server_ClientDisconnected;

                        await hotReloadServer.StartAsync(port);

                        Debug.WriteLine($"HotReloadKit tcp port: {port}");
                        break;
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine(ex.Message);
                        if (hotReloadServer != null) await hotReloadServer!.StopAsync();
                    }
                }
            }
        }

        void Server_ClientConnected(SlimClient client)
        {
            Debug.WriteLine($"HotReloadKit client connected guid: {client.Guid}");
            ClientRunLoop(client);
        }

        void Server_ClientDisconnected(SlimClient client)
        {
            Debug.WriteLine($"HotReloadKit client disconnected guid: {client.Guid}");
        }

        public void ClientRunLoop(SlimClient client)
        {
            _ = Task.Run(async () =>
            {
                var serverConnectionData = new HotReloadServerConnectionData
                {
                    Token = HotReloadServerConnectionData.DefaultToken,
                    Version = HotReloadServerConnectionData.CurrentVersion,
                    Guid = client.Guid
                };
                await client.WriteAsync(JsonSerializer.Serialize(serverConnectionData));

                var connectionMessage = await client.ReadAsync(defaultConnectionResponseReadTimeout);
                var ConnectionData = JsonSerializer.Deserialize<HotReloadClientConnectionData>(connectionMessage);
                if (ConnectionData != null && ConnectionData.Type == nameof(HotReloadClientConnectionData))
                {
                    Debug.WriteLine($"HotReloadKit session assembly: {ConnectionData.AssemblyName} platform: {ConnectionData.PlatformName}");

                    DebugContext? context = null;
                    while (context == null && client.IsConnected && !cancellationTokenSource.Token.IsCancellationRequested)
                    {
                        lock (activeContexts)
                        {
                            context = activeContexts.FirstOrDefault(e => e.Value.ProjectInfo.Project.AssemblyName == ConnectionData.AssemblyName && e.Value.ClientGuid == null).Value;
                        }
                        await Task.Delay(100);
                    }
                    if (context != null)
                    {
                        context.ClientGuid = client.Guid;
                        context.changedFilesSemaphoreTrig = new SemaphoreSlim(0);

                        // clear list
                        lock (context.changedFilePaths)
                        {
                            context.changedFilePaths.Clear();
                        }

                        if (context.HotReloadStarted != null) context.HotReloadStarted();

                        // client loop
                        while (client.IsConnected && !cancellationTokenSource.Token.IsCancellationRequested)
                        {
                            try
                            {
                                var changedFilesSemaphoreTrig = context.changedFilesSemaphoreTrig;
                                await changedFilesSemaphoreTrig.WaitAsync(cancellationTokenSource.Token);

                                await client.WriteAsync(JsonSerializer.Serialize(new HotReloadRequest()));

                                var message = await client.ReadAsync();
                                var additionaTypesMessage = JsonSerializer.Deserialize<HotReloadRequestAdditionalTypesMessage>(message) ?? new HotReloadRequestAdditionalTypesMessage();

                                await CompileAndEmitChangesAsync(client, context, additionaTypesMessage.TypeNames);
                            }
                            catch (Exception ex)
                            {
                                Debug.WriteLine(ex.Message);
                            }
                        }

                        if (context.HotReloadStopped != null) context.HotReloadStopped();
                    }
                }
            });
        }

        async Task CompileAndEmitChangesAsync(SlimClient client, DebugContext context, string[] additionaTypeNames)
        {
            try
            {
                context.asseblyVersion++;

                // ------- compilation ---------

                CodeCompilation? codeCompilation = null;

                lock (context.changedFilePaths)
                {
                    if (context.changedFilePaths.Count > 0)
                    {
                        codeCompilation = new CodeCompilation
                        {
                            AdditionalTypeNames = additionaTypeNames,
                            Solution = null, //Solution,
                            Project = context.ProjectInfo.Project,
                            OutputFilePath = context.ProjectInfo.OutputFilePath,
                            RequestedFilePaths = context.changedFilePaths.ToList()
                        };
                        context.changedFilePaths.Clear();
                    }
                }

                if (codeCompilation != null)
                {
                    await codeCompilation.CompileAsync();

                    // ------- send data assembly ---------
                    await codeCompilation.EmitDataAsync(async (string[] typeNames, string[] changedTypeNames, byte[] dllData, byte[] pdbData) =>
                    {
                        var hotReloadData = new HotReloadData
                        {
                            TypeNames = typeNames,
                            DllData = dllData,
                            PdbData = pdbData,
                            ChangedTypeNames = changedTypeNames
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

        public void AddChangedFile(string projectPath, string fileName)
        {
            _ = Task.Run(() =>
            {
                var context = activeContexts[projectPath];
                lock (context.changedFilePaths)
                {
                    if (!context.changedFilePaths.Contains(fileName))
                        context.changedFilePaths.Add(fileName);
                }
            });
        }

        static object releaseLock = new object();
        public void TrigChangedFiles(string projectPath)
        {
            lock (releaseLock)
            {
                var changedFilesSemaphoreTrig = activeContexts[projectPath].changedFilesSemaphoreTrig;
                if (changedFilesSemaphoreTrig.CurrentCount == 0)
                    changedFilesSemaphoreTrig.Release();
            }
        }
    }
}