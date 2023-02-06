using System.Net;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Diagnostics;
using SlimTcpServer;
using HotReloadKit.Shared;

namespace HotReloadKit;

public static class HotReloader
{
    // public

    public const int DefaultTimeout = 10000;

    public static Action<Type[]>? UpdateApplication { get; set; }
    public static Func<string[]>? RequestAdditionalTypeNames { get; set; }

    // private

    static readonly int[] serverPorts = new int[] { 50888, 50889, 5088, 5089, 60088, 60888 };
    static readonly HotReloadClientConnectionData ClientConnectionData = new HotReloadClientConnectionData();

    // tokens

    public static void Init<T>(IPAddress[] serverIPs, int timeout = DefaultTimeout, string? platformName = null)
    {
        ClientConnectionData.PlatformName = platformName;
        ClientConnectionData.AssemblyName = typeof(T).Assembly.GetName().Name;
        _ = ConnectAsync(serverIPs, timeout);
    }

    static async Task ConnectAsync(IPAddress[] serverIPs, int timeout = DefaultTimeout)
    {
        foreach (var serverIP in serverIPs)
            foreach (var serverPort in serverPorts)
            {
                try
                {
                    var client = new SlimClient();

                    client.Disconnected += client => Debug.WriteLine("HotReloadKit - disconnected");

                    await client.ConnectAsync(serverIP, serverPort);

                    var message = await client.ReadAsync(DefaultTimeout);
                    var messageData = JsonSerializer.Deserialize<HotReloadServerConnectionData>(message);
                    if (messageData?.Token == HotReloadServerConnectionData.DefaultToken)
                    {
                        Debug.WriteLine($"HotReloadKit connected - address: {serverIP.ToString()} port: {serverPort} server version: {messageData.Version} guid: {messageData.Guid}");
                        _ = ClientRunLoop(client);
                        return;
                    }
                    else
                        throw new Exception("HotReloadKit - wrong server token");
                }                
                catch (Exception ex)
                {
                    Debug.WriteLine(ex.Message);
                }
                Debug.WriteLine($"HotReloadKit - address: {serverIP.ToString()} port: {serverPort} success: NO");
            }
        Debug.WriteLine($"HotReloadKit - no connection to the IDE.");
    }

    static async Task ClientRunLoop(SlimClient client)
    {
        try
        {
            await client.WriteAsync(JsonSerializer.Serialize(ClientConnectionData));

            while (client.IsConnected)
            {
                var message = await client.ReadAsync();
                var messageData = JsonSerializer.Deserialize<HotReloadMessage>(message);
                if (messageData?.Type == nameof(HotReloadRequest))
                {
                    Debug.WriteLine("HotReloadKit - hot reload requested");

                    string[] requestedTypeNames = RequestAdditionalTypeNames?.Invoke() ?? Array.Empty<string>();
                    var hotreloadRequest = new HotReloadRequestAdditionalTypesMessage { TypeNames = requestedTypeNames };
                    var jsonRequest = JsonSerializer.Serialize(hotreloadRequest);

                    await client.WriteAsync(jsonRequest);
                }
                else if (messageData?.Type == nameof(HotReloadData))
                {
                    var hotReloadData = JsonSerializer.Deserialize<HotReloadData>(message)!;
                    var assembly = Assembly.Load(hotReloadData.DllData!, hotReloadData.PdbData);

                    var typeList = new List<Type>();
                    foreach (var typeName in hotReloadData.TypeNames!)
                    {
                        var type = assembly.GetType(typeName);
                        if (type != null) typeList.Add(type);
                    }
                    UpdateApplication?.Invoke(typeList.ToArray());
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex.Message);
        }
    }
}