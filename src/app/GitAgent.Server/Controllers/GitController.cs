using Microsoft.AspNetCore.Mvc;
using GitAgent.Shared.DTOs;
using GitAgent.Server.Services;
using Serilog;

namespace GitAgent.Server.Controllers;

[ApiController]
[Route("api/[controller]")]
public class GitController : ControllerBase
{
    private readonly IWorkerService _workerService;

    public GitController(IWorkerService workerService)
    {
        _workerService = workerService;
    }

    [HttpGet("status")]
    public IActionResult GetWorkerStatus()
    {
        return Ok(new { connected = _workerService.IsWorkerConnected() });
    }

    [HttpGet("repositories")]
    public async Task<IActionResult> GetRepositories()
    {
        try
        {
            if (!_workerService.IsWorkerConnected())
            {
                return ServiceUnavailable("Worker is offline");
            }

            var repos = await _workerService.GetRepositoryConfigs();
            return Ok(repos);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error getting repository configurations");
            return StatusCode(500, new { error = ex.Message });
        }
    }

    [HttpGet("branches/{repositoryName}")]
    public async Task<IActionResult> GetBranches(string repositoryName)
    {
        try
        {
            if (!_workerService.IsWorkerConnected())
            {
                return ServiceUnavailable("Worker is offline");
            }

            var branches = await _workerService.GetBranches(repositoryName);
            return Ok(branches);
        }
        catch (Exception ex)
        {
            Log.Error(ex, $"Error getting branches for {repositoryName}");
            return StatusCode(500, new { error = ex.Message });
        }
    }

    [HttpGet("commits/{repository}/{branch}")]
    public async Task<IActionResult> GetCommits(string repository, string branch, [FromQuery] int count = 10)
    {
        try
        {
            if (!_workerService.IsWorkerConnected())
            {
                return ServiceUnavailable("Worker is offline");
            }

            var commits = await _workerService.GetBranchCommits(branch, repository, count);
            return Ok(commits);
        }
        catch (Exception ex)
        {
            Log.Error(ex, $"Error getting commits for {repository}/{branch}");
            return StatusCode(500, new { error = ex.Message });
        }
    }

    [HttpPost("push")]
    public async Task<IActionResult> PushBranch([FromBody] PushBranchRequest request)
    {
        try
        {
            if (!_workerService.IsWorkerConnected())
            {
                return ServiceUnavailable("Worker is offline");
            }

            var result = await _workerService.PushBranch(request);
            return Ok(result);
        }
        catch (Exception ex)
        {
            Log.Error(ex, $"Error pushing branch {request.Branch} from {request.FromRepository} to {request.ToRepository}");
            return StatusCode(500, new { error = ex.Message });
        }
    }

    private ObjectResult ServiceUnavailable(string message)
    {
        return StatusCode(503, new { error = message });
    }
}
