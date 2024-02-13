using System.Net;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Diagnostics;
using SlimTcpServer;
using HotReloadKit.Shared;

namespace HotReloadKit;

public class HotReloadTypeData
{
    public Type Type { get; set; }
    public bool IsFromChangedFile { get; set; }
}

public static class HotReloader
{
    // public

    public const int DefaultTimeout = 1000;

    public static Action<HotReloadTypeData[]> UpdateApplication { get; set; }
    public static Func<string[]> RequestAdditionalTypes { get; set; }

    // private

    static readonly int[] serverPorts = new int[] { 5088, 5089, 5994, 5995, 5996, 5997, 5998 };
    static readonly HotReloadClientConnectionData ClientConnectionData = new HotReloadClientConnectionData();

    // tokens

    public static void Init<T>(IPAddress[] serverIPs, int timeout = DefaultTimeout, string platformName = null)
    {
        ClientConnectionData.PlatformName = platformName;
        ClientConnectionData.AssemblyName = typeof(T).Assembly.GetName().Name;
        _ = ConnectAsync(serverIPs, timeout);
    }

    static async Task ConnectAsync(IPAddress[] serverIPs, int timeout)
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
                        Debug.WriteLine($"HotReloadKit connected - address: {serverIP.ToString()} port: {serverPort} protocol ver: {messageData.Version} guid: {messageData.Guid}");
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

                    string[] requestedTypeNames = RequestAdditionalTypes?.Invoke() ?? Array.Empty<string>();
                    var hotreloadRequest = new HotReloadRequestAdditionalTypesMessage { TypeNames = requestedTypeNames };
                    var jsonRequest = JsonSerializer.Serialize(hotreloadRequest);

                    await client.WriteAsync(jsonRequest);
                }
                else if (messageData?.Type == nameof(HotReloadData))
                {
                    var hotReloadData = JsonSerializer.Deserialize<HotReloadData>(message)!;
                    var assembly = Assembly.Load(hotReloadData.DllData!, hotReloadData.PdbData);

                    var typeDataList = new List<HotReloadTypeData>();
                    foreach (var typeName in hotReloadData.TypeNames)
                    {
                        var type = assembly.GetType(typeName);
                        var isFromChangedFile = false;
                        if (hotReloadData.ChangedTypeNames != null)                        
                            isFromChangedFile = hotReloadData.ChangedTypeNames.Contains(typeName);
                        if (type != null) typeDataList.Add(new HotReloadTypeData { Type = type, IsFromChangedFile = isFromChangedFile });
                    }
                    UpdateApplication?.Invoke(typeDataList.ToArray());
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex.Message);
        }
    }
}
