using Microsoft.AspNetCore.Mvc;
using System.Net.Sockets;
using HotReloadKit.Server;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;

[Route("api")]
[ApiController]
public class ApiController : ControllerBase
{
    Dictionary<string, ProjectInfo> activeProjects = new Dictionary<string, ProjectInfo>();

    HotReloadServer hotReloadServer;

    [HttpPost("debugStarted")]
    public async Task<IActionResult> DebugStartedAsync([FromBody] DebugInfo data)
    {
        try
        {
            var workspace = MSBuildWorkspace.Create();
            var project = await workspace.OpenProjectAsync(data.ProjectPath);

            activeProjects[data.ProjectPath] = new ProjectInfo
            {
                DebugInfo = data,
                Project = project,
                Workspace = workspace
            };

            return Ok("DebugInfo received and processed successfully.");
        }
        catch (System.Exception ex)
        {            
            throw;
        }
        
    }

    [HttpPost("fileChanged")]
    public IActionResult FileChanged([FromBody] FileChangedInfo data)
    {
        return Ok("File changed received and processed");
    }

    SemaphoreSlim sessionSemaphore = new SemaphoreSlim(1);
    async Task RunHotReloadServer()
    {
        await sessionSemaphore.WaitAsync();

        hotReloadServer = new HotReloadServer();
        hotReloadServer.HotReloadStarted += StartHotReloadSession;
        hotReloadServer.HotReloadStopped += StopHotReloadSession;

        await hotReloadServer.StartServerAsync();

        await Task.Delay(1000);
        await IdeServices.ProjectOperations.CurrentRunOperation.Task;
        await hotReloadServer.StopServerAsync();

        sessionSemaphore.Release();
    }

    
}

public class FileChangedInfo
{
    public required string ProjectPath { get; set; }
    public required string FilePath { get; set; }
}

public class ProjectInfo
{
    public required DebugInfo DebugInfo { get; set; }
    public required MSBuildWorkspace Workspace { get; set; }
    public required Project Project { get; set; }
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