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


    Platform GetPlatform(DebugInfo debugInfo) 
    {
        if (debugInfo.RuntimeIdentifier.EndsWith("arm64")) return Platform.Arm64;
        return Platform.X64;
    }

    [HttpGet("startService")]
    public async Task<IActionResult> StartServiceAsync()
    {
        if (hotReloadServer == null)
        {
            hotReloadServer = new HotReloadServer();
            await hotReloadServer.StartServerAsync();
            Microsoft.Build.Locator.MSBuildLocator.RegisterDefaults();
            return Ok($"HotReloadKit service started");
        }
        return BadRequest();
    }

    [HttpGet("checkService")]
    public IActionResult CheckService()
    {
        return Ok($"HotReloadKit service is working");
    }

    [HttpPost("debugStarted")]
    public async Task<IActionResult> DebugStartedAsync([FromBody] DebugInfo debugInfo)
    {
        try
        {
            if (hotReloadServer != null)
            {
                var workspace = MSBuildWorkspace.Create();
                var project = await workspace.OpenProjectAsync(debugInfo.ProjectPath);

                if (debugInfo.Type == "maui")
                {
                    string outputFilePath;
                    
                    if (debugInfo.RuntimeIdentifier != "undefined")
                    {
                        var platform = GetPlatform(debugInfo);
                        var options = project.CompilationOptions!.WithPlatform(platform);
                        project = project.WithCompilationOptions(options);
                        outputFilePath = Path.Combine(Path.GetDirectoryName(debugInfo.ProjectPath)!, "bin", debugInfo.Configuration, debugInfo.TargetFramework, debugInfo.RuntimeIdentifier, project.AssemblyName + ".dll");
                    }
                    else
                    {
                        outputFilePath = Path.Combine(Path.GetDirectoryName(debugInfo.ProjectPath)!, "bin", debugInfo.Configuration, debugInfo.TargetFramework, project.AssemblyName + ".dll");
                    }

                    hotReloadServer.RegisterProject(new ProjectInfo
                    {
                        DebugInfo = debugInfo,
                        Project = project,
                        OutputFilePath = outputFilePath
                    });
                }
                else
                {
                    hotReloadServer.RegisterProject(new ProjectInfo
                    {
                        DebugInfo = debugInfo,
                        Project = project,
                    });
                }
            }

            return Ok($"Debug started");
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
        return Ok($"Debug terminated");
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
    // public required MSBuildWorkspace Workspace { get; set; }
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