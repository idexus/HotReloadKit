using System.Net;
using System.Reflection;
using System.Text.Json;
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

    public const int DefaultTimeout = 1000;

    public static Action<Type[]>? UpdateApplication { get; set; }
    public static Func<string[]>? RequestedTypeNamesHandler { get; set; }

    // private

    const string serverToken = "<<HotReloadKit>>";

    static int[] serverPorts = new int[] { 5088, 5089, 50888, 50889 };
    static Type? projectType;

    public static void Init<T>(IPAddress[] serverIPs, int timeout = DefaultTimeout)
    {
        projectType = typeof(T);
        _ = ConnectAsync(serverIPs, timeout);
    }

    static async Task ConnectAsync(IPAddress[] serverIPs, int timeout = DefaultTimeout)
    {
        await Task.Delay(1000);

        var client = new SlimClient();

        client.ClientDisconnected += client => Console.WriteLine("Hot reload disconnected");
        client.ConnectedToServerEndPoint += (bool success, IPAddress serverIP, int serverPort)
            => Console.WriteLine($"Hot reload connected to the end point: {(success ? "YES" : "NO")} address: {serverIP} port: {serverPort}");

        try
        {
            await client.Connect(serverIPs, serverPorts, timeout);
            var readServerToken = await client.ReadAsync();
            if (readServerToken != serverToken) client.Disconnect();
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.Message);
        }

        _ = ClientRunLoop(client);
    }

    static async Task ClientRunLoop(SlimClient client)
    {
        var projectAssemblyName = projectType?.Assembly.GetName().Name ?? "----";
        var reloadToken = $@"<<|hotreload|{projectAssemblyName}|hotreload|>>";

        try
        {
            while (client.IsConnected)
            {
                var message = await client.ReadAsync();
                if (message == reloadToken)
                {
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
#pragma warning disable CS0168
        catch (Exception ex)
        {

        }
#pragma warning restore CS0168
    }
}