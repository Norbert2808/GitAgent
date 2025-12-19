using Microsoft.AspNetCore.SignalR;
using GitAgent.Shared.Models;
using GitAgent.Shared.DTOs;
using GitAgent.Shared.Interfaces;
using GitAgent.Server.Hubs;
using Serilog;

namespace GitAgent.Server.Services;

public class WorkerService : IWorkerService
{
    private readonly IHubContext<GitWorkerHub, IWorkerClient> _hubContext;
    private const int OperationTimeoutSeconds = 30;

    public WorkerService(IHubContext<GitWorkerHub, IWorkerClient> hubContext)
    {
        _hubContext = hubContext;
    }

    public bool IsWorkerConnected()
    {
        return GitWorkerHub.IsWorkerConnected;
    }

    public async Task<List<RepositoryInfo>> GetRepositoryConfigs()
    {
        EnsureWorkerConnected();

        var worker = _hubContext.Clients.Client(GitWorkerHub.WorkerConnectionId!);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(OperationTimeoutSeconds));
        try
        {
            Log.Information("Requesting repository configurations from worker");
            return await worker.GetRepositoryConfigs();
        }
        catch (OperationCanceledException)
        {
            Log.Error("Operation timed out while getting repository configs");
            throw new TimeoutException("Worker operation timed out");
        }
    }

    public async Task<List<BranchInfo>> GetBranches(string repositoryName)
    {
        EnsureWorkerConnected();

        var worker = _hubContext.Clients.Client(GitWorkerHub.WorkerConnectionId!);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(OperationTimeoutSeconds));
        try
        {
            Log.Information($"Requesting branches for {repositoryName} from worker");
            return await worker.GetBranches(repositoryName);
        }
        catch (OperationCanceledException)
        {
            Log.Error($"Operation timed out while getting branches for {repositoryName}");
            throw new TimeoutException("Worker operation timed out");
        }
    }

    public async Task<List<CommitInfo>> GetBranchCommits(string branchName, string repositoryName, int count = 10)
    {
        EnsureWorkerConnected();

        var worker = _hubContext.Clients.Client(GitWorkerHub.WorkerConnectionId!);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(OperationTimeoutSeconds));
        try
        {
            Log.Information($"Requesting commits for {repositoryName}/{branchName} from worker");
            var request = new GetCommitsRequest
            {
                Repository = repositoryName,
                Branch = branchName,
                Count = count
            };
            return await worker.GetCommits(request);
        }
        catch (OperationCanceledException)
        {
            Log.Error("Operation timed out while getting commits");
            throw new TimeoutException("Worker operation timed out");
        }
    }

    public async Task<SyncResult> PushBranch(PushBranchRequest request)
    {
        EnsureWorkerConnected();

        var worker = _hubContext.Clients.Client(GitWorkerHub.WorkerConnectionId!);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(OperationTimeoutSeconds));
        try
        {
            Log.Information($"Requesting push from {request.FromRepository} to {request.ToRepository}: {request.Branch} (force: {request.Force})");
            return await worker.PushBranch(request);
        }
        catch (OperationCanceledException)
        {
            Log.Error($"Operation timed out while pushing from {request.FromRepository} to {request.ToRepository}");
            throw new TimeoutException("Worker operation timed out");
        }
    }

    private void EnsureWorkerConnected()
    {
        if (!IsWorkerConnected())
        {
            Log.Error("Worker is not connected");
            throw new InvalidOperationException("Worker is offline. Please ensure the Git Worker is running on your local machine.");
        }
    }
}
