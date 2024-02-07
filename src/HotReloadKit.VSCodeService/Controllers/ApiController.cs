using Microsoft.AspNetCore.Mvc;
using System.Net.Sockets;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;
using SlimTcpServer;
using System.Diagnostics;
using HotReloadKit.Server;


[Route("api")]
[ApiController]
public class ApiController : ControllerBase
{
    static HotReloadServer? hotReloadServer;


    Platform GetPlatform(DebugInfo debugInfo) => (debugInfo.RuntimeIdentifier) switch
    {
        "maccatalyst-x64" => Platform.X64,
        "maccatalyst-arm64" => Platform.Arm64,
        _ => Platform.X64
    };

    [HttpPost("debugStarted")]
    public async Task<IActionResult> DebugStartedAsync([FromBody] DebugInfo debugInfo)
    {
        try
        {
            if (hotReloadServer == null)
            {
                hotReloadServer = new HotReloadServer();
                await hotReloadServer.StartServerAsync();
                Microsoft.Build.Locator.MSBuildLocator.RegisterDefaults();
            }
            var workspace = MSBuildWorkspace.Create();
            var project = await workspace.OpenProjectAsync(debugInfo.ProjectPath);

            project.CompilationOptions?.WithPlatform(GetPlatform(debugInfo));
            var platform = GetPlatform(debugInfo);
            var options = project.CompilationOptions!.WithPlatform(platform);
            var updatedProject = project.WithCompilationOptions(options);
            var outputFilePath = Path.Combine(Path.GetDirectoryName(debugInfo.ProjectPath)!, "bin", debugInfo.Configuration, debugInfo.TargetFramework, debugInfo.RuntimeIdentifier, project.AssemblyName + ".dll");
            hotReloadServer.RegisterProject(new ProjectInfo
            {
                DebugInfo = debugInfo,
                Project = project,
                Workspace = workspace,
                OutputFilePath = outputFilePath
            });

            return Ok($"Debug started - project {debugInfo.ProjectPath}");
        }
        catch (System.Exception)
        {            
            throw;
        }
    }

    [HttpPost("fileChanged")]
    public IActionResult FileChanged([FromBody] FileChangedInfo data)
    {
        hotReloadServer?.AddChangedFile(data.ProjectPath, data.FilePath);
        hotReloadServer?.TrigChangedFiles(data.ProjectPath);
        return Ok("File changed received and processed");
    }

    [HttpPost("debugTerminated")]
    public IActionResult DebugTerminated([FromBody] DebugTerminatedInfo data)
    {
        hotReloadServer?.UnregisterProject(data.ProjectPath);
        return Ok($"Debug terminated - {data.ProjectPath}");
    }
    // ------------------
}

public class FileChangedInfo
{
    public required string ProjectPath { get; set; }
    public required string FilePath { get; set; }
}

public class DebugTerminatedInfo
{
    public required string ProjectPath { get; set; }
}

public class ProjectInfo
{
    public required DebugInfo DebugInfo { get; set; }
    public required MSBuildWorkspace Workspace { get; set; }
    public required Project Project { get; set; }
    public string? OutputFilePath { get; set; }
}

public class DebugContext
{
    public required ProjectInfo ProjectInfo { get; set; }
    public SemaphoreSlim changedFilesSemaphoreTrig = new SemaphoreSlim(0);
    public int asseblyVersion = 0;
    public readonly List<string> changedFilePaths = new List<string>();
    public Action? HotReloadStarted;
    public Action? HotReloadStopped;
    public Guid? ClientGuid;
}

public class DebugInfo
{
    public required string Configuration { get; set; }
    public required string Type { get; set; }
    public required string ProjectPath { get; set; }
    public required string WorkspaceDirectory { get; set; }
    public required string RuntimeIdentifier { get; set; }
    public required string TargetFramework { get; set; }
    public required string Platform { get; set; }
}