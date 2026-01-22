using Microsoft.AspNetCore.SignalR.Client;
using Serilog;
using GitAgent.Shared;
using GitAgent.Shared.Models;
using GitAgent.Shared.Interfaces;
using GitAgent.Worker.Services;
using GitAgent.Shared.DTOs;

namespace GitAgent.Worker.SignalR;

public class GitServerConnection
{
    private readonly HubConnection _connection;
    private readonly IGitAgentService _gitSyncService;
    private bool _isConnected;

    public GitServerConnection(string serverUrl, string apiKey, IGitAgentService gitSyncService)
    {
        _gitSyncService = gitSyncService;

        _connection = new HubConnectionBuilder()
            .WithUrl($"{serverUrl}/workerhub", options =>
            {
                options.Headers[Constants.ApiKeyHeaderName] = apiKey;
            })
            .WithAutomaticReconnect([TimeSpan.Zero, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10)])
            .Build();

        // Increase message size limit for large branch lists
        _connection.ServerTimeout = TimeSpan.FromMinutes(2);
        _connection.HandshakeTimeout = TimeSpan.FromSeconds(30);
        _connection.KeepAliveInterval = TimeSpan.FromSeconds(15);

        RegisterHandlers();
        SetupConnectionEvents();
    }

    private void RegisterHandlers()
    {
        // Register methods that Server can call on Worker
        _connection.On(nameof(IWorkerClient.GetRepositoryConfigs), () =>
        {
            Log.Information("Server requested repository configurations");
            return _gitSyncService.GetRepositoryConfigs();
        });

        _connection.On<string, List<BranchInfo>>(nameof(IWorkerClient.GetBranches), async (repositoryName) =>
        {
            try
            {
                Log.Information($"Server requested branches for {repositoryName}");
                var result = await _gitSyncService.GetBranches(repositoryName);
                Log.Information($"Returning {result.Count} branches for {repositoryName}");
                return result;
            }
            catch (Exception ex)
            {
                Log.Error(ex, $"Error getting branches for {repositoryName}");
                throw;
            }
        });

        _connection.On<GetCommitsRequest, List<CommitInfo>>(nameof(IWorkerClient.GetCommits), async (request) =>
        {
            Log.Information($"Server requested commits for {request.Repository}/{request.Branch}");
            return await _gitSyncService.GetBranchCommits(request.Branch, request.Repository, request.Count);
        });

        _connection.On<PushBranchRequest, SyncResult>(nameof(IWorkerClient.PushBranch), async (request) =>
        {
            Log.Information($"Server requested push from {request.FromRepository} to {request.ToRepository}: {request.Branch} (force: {request.Force})");
            return await _gitSyncService.PushBranch(request.Branch, request.FromRepository, request.ToRepository, request.Force);
        });
    }

    private void SetupConnectionEvents()
    {
        _connection.Closed += async (error) =>
        {
            _isConnected = false;
            Log.Warning("Connection to server closed. Error: {Error}", error?.Message ?? "None");
            await Task.CompletedTask;
        };

        _connection.Reconnecting += async (error) =>
        {
            _isConnected = false;
            Log.Warning("Attempting to reconnect to server. Error: {Error}", error?.Message ?? "None");
            await Task.CompletedTask;
        };

        _connection.Reconnected += async (connectionId) =>
        {
            _isConnected = true;
            Log.Information("Reconnected to server. Connection ID: {ConnectionId}", connectionId ?? "Unknown");
            await Task.CompletedTask;
        };
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        var retryDelays = new[] { 2, 5, 10, 15, 30 }; // seconds
        var attemptCount = 0;

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                attemptCount++;
                Log.Information("Connecting to server... (Attempt {Attempt})", attemptCount);
                await _connection.StartAsync(cancellationToken);
                _isConnected = true;
                Log.Information("✅ Connected to server successfully. Connection ID: {ConnectionId}", _connection.ConnectionId);
                return;
            }
            catch (Exception ex)
            {
                _isConnected = false;

                var delaySeconds = retryDelays[Math.Min(attemptCount - 1, retryDelays.Length - 1)];
                Log.Warning(ex, "Failed to connect to server (Attempt {Attempt}). Retrying in {Delay} seconds...", attemptCount, delaySeconds);

                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(delaySeconds), cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    Log.Information("Connection attempts cancelled by user");
                    throw;
                }
            }
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            Log.Information("Disconnecting from server...");
            await _connection.StopAsync(cancellationToken);
            _isConnected = false;
            Log.Information("Disconnected from server");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error while disconnecting from server");
        }
    }

    public bool IsConnected => _isConnected && _connection.State == HubConnectionState.Connected;
}
