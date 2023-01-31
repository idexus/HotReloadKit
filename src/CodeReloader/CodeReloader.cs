using System.Net;
using System.Reflection;
using System.Text.Json;
using TcpServerSlim;

namespace CodeReloadSupport;

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
    public const int DefaultServerPort = 9988;
    public const int DefaultTimeout = 1000;

    public static Action<Type[]>? UpdateApplication { get; set; }
    public static Func<string[]>? RequestedTypeNamesHandler { get; set; }

    static Type? projectType;

    public static void Init<T>(IPAddress[] serverIPs, int serverPort = DefaultServerPort, int timeout = DefaultTimeout)
    {
        projectType = typeof(T);
        Task.Run(async () =>
        {
            var client = new TcpClientSlim();

            client.DataReceived += Client_DataReceived;
            client.ClientConnected += client => Console.WriteLine("Hot reload connected");
            client.ClientDisconnected += client => Console.WriteLine("Hot reload disconnected");

            try
            {
                await client.Connect(serverIPs);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
        });
    }

    static SemaphoreSlim semaphoreSlim = new SemaphoreSlim(1);
    static void Client_DataReceived(TcpClientSlim client, string message)
    {
        Task.Run(async () =>
        {
            await semaphoreSlim.WaitAsync();
            try
            {
                var assemblyName = projectType?.Assembly.GetName().Name ?? "----";
                var reloadToken = $@"<<|hotreload|{assemblyName}|hotreload|>>";

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
#pragma warning disable CS0168
            catch (Exception ex)
            {

            }
#pragma warning restore CS0168

            semaphoreSlim.Release();
        });
    }
}