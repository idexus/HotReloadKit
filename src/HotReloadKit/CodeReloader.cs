using System.Net;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Diagnostics;
using SlimMessenger;

namespace HotReloadKit;

class HotReloadRequest
{
    public string[]? TypeNames { get; set; }
}

class HotReloadData
{
    public string[]? TypeNames { get; set; }
    public byte[]? AssemblyData { get; set; }
    public byte[]? PdbData { get; set; }
}

public static class CodeReloader
{
    // public

    public const int DefaultTimeout = 10000;

    public static Action<Type[]>? UpdateApplication { get; set; }
    public static Func<string[]>? RequestedTypeNamesHandler { get; set; }

    // private

    static int[] serverPorts = new int[] { 50888, 50889, 5088, 5089, 60088, 60888 };
    static Type? projectType;

    // tokens

    static string projectAssemblyName => projectType?.Assembly.GetName().Name ?? "----";
    static string reloadToken => $@"<<|HotReloadKit|{projectAssemblyName}|hotreload|>>";
    static string serverToken => $@"<<|HotReloadKit|{projectAssemblyName}|connect|>>";

    public static void Init<T>(IPAddress[] serverIPs, int timeout = DefaultTimeout)
    {
        projectType = typeof(T);
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

                    await client.Connect(serverIP, serverPort, timeout);

                    var readServerToken = await client.ReadAsync(DefaultTimeout);
                    if (readServerToken == serverToken)
                    {
                        Console.WriteLine($"HotReloadKit connected - address: {serverIP.ToString()} port: {serverPort}");
                        _ = ClientRunLoop(client);
                        return;
                    }
                    else
                        throw new Exception("HotReloadKit - wrong token");
                }                
                catch (Exception ex)
                {
                    Debug.WriteLine(ex.Message);
                }
                Debug.WriteLine($"HotReloadKit - address: {serverIP.ToString()} port: {serverPort} success: NO");
            }
        Console.WriteLine($"HotReloadKit - no connection to the IDE.");
    }

    static async Task ClientRunLoop(SlimClient client)
    {
        try
        {
            while (client.IsConnected)
            {
                var message = await client.ReadAsync();
                if (message == reloadToken)
                {
                    Debug.WriteLine("HotReloadKit - hot reload requested");

                    string[] requestedTypeNames = RequestedTypeNamesHandler?.Invoke() ?? Array.Empty<string>();
                    var hotreloadRequest = new HotReloadRequest { TypeNames = requestedTypeNames };
                    var jsonRequest = JsonSerializer.Serialize(hotreloadRequest);

                    await client.WriteAsync(jsonRequest);
                }
                else
                {
                    var hotReloadData = JsonSerializer.Deserialize<HotReloadData>(message)!;
                    var assembly = Assembly.Load(hotReloadData.AssemblyData!, hotReloadData.PdbData);

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