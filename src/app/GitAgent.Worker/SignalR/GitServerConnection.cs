using Microsoft.AspNetCore.SignalR.Client;
using Serilog;
using GitAgent.Shared;
using GitAgent.Shared.Models;
using GitAgent.Shared.Interfaces;
using GitAgent.Worker.Services;
using GitAgent.Shared.DTOs;
using GitAgent.Worker.Configuration;
using Microsoft.Extensions.Logging;

namespace GitAgent.Worker.SignalR;

public class GitServerConnection
{
    private readonly HubConnection _connection;
    private readonly IGitAgentService _gitSyncService;
    private readonly int[] _reconnectDelays;
    private bool _isConnected;

    public GitServerConnection(string serverUrl, string apiKey, IGitAgentService gitSyncService, TimeoutConfig timeouts)
    {
        _gitSyncService = gitSyncService;
        _reconnectDelays = timeouts.ReconnectDelaysSeconds;

        var reconnectDelays = timeouts.ReconnectDelaysSeconds.Select(seconds => TimeSpan.FromSeconds(seconds)).ToArray();

        _connection = new HubConnectionBuilder()
            .WithUrl($"{serverUrl}/workerhub", options =>
            {
                options.Headers[Constants.ApiKeyHeaderName] = apiKey;
            })
            .WithAutomaticReconnect(reconnectDelays)
            .ConfigureLogging(logging =>
            {
                logging.SetMinimumLevel(LogLevel.Warning);
            })
            .Build();

        _connection.ServerTimeout = TimeSpan.FromSeconds(timeouts.SignalR.ServerTimeoutSeconds);
        _connection.HandshakeTimeout = TimeSpan.FromSeconds(timeouts.SignalR.HandshakeTimeoutSeconds);
        _connection.KeepAliveInterval = TimeSpan.FromSeconds(timeouts.SignalR.KeepAliveIntervalSeconds);

        RegisterHandlers();
        SetupConnectionEvents();
    }

    private void RegisterHandlers()
    {
        // Register methods that Server can call on Worker
        _connection.On(nameof(IWorkerClient.GetRepositoryConfigs), () =>
        {
            try
            {
                Log.Information("Server requested repository configurations");
                var result = _gitSyncService.GetRepositoryConfigs();
                Log.Information($"Returned {result.Count} repository configurations");
                return result;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "ERROR in GetRepositoryConfigs handler");
                throw;
            }
        });

        _connection.On<string, List<BranchInfo>>(nameof(IWorkerClient.GetBranches), async (repositoryName) =>
        {
            try
            {
                Log.Information($"Server requested branches for {repositoryName}");
                var result = await _gitSyncService.GetBranches(repositoryName);
                Log.Information($"Successfully retrieved {result.Count} branches for {repositoryName}");
                return result;
            }
            catch (Exception ex)
            {
                Log.Error(ex, $"ERROR in GetBranches handler for {repositoryName}");
                throw;
            }
        });

        _connection.On<GetCommitsRequest, List<CommitInfo>>(nameof(IWorkerClient.GetCommits), async (request) =>
        {
            try
            {
                Log.Information($"Server requested commits for {request.Repository}/{request.Branch}");
                var result = await _gitSyncService.GetBranchCommits(request.Branch, request.Repository, request.Count);
                Log.Information($"Returned {result.Count} commits for {request.Repository}/{request.Branch}");
                return result;
            }
            catch (Exception ex)
            {
                Log.Error(ex, $"ERROR in GetCommits handler for {request.Repository}/{request.Branch}");
                throw;
            }
        });

        _connection.On<PushBranchRequest, SyncResult>(nameof(IWorkerClient.PushBranch), async (request) =>
        {
            try
            {
                Log.Information($"Server requested push from {request.FromRepository} to {request.ToRepository}: {request.Branch} (force: {request.Force})");
                var result = await _gitSyncService.PushBranch(request.Branch, request.FromRepository, request.ToRepository, request.Force);
                Log.Information($"Push completed: {result.Success} - {result.Message}");
                return result;
            }
            catch (Exception ex)
            {
                Log.Error(ex, $"ERROR in PushBranch handler for {request.Branch}");
                throw;
            }
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

                var delaySeconds = _reconnectDelays[Math.Min(attemptCount - 1, _reconnectDelays.Length - 1)];
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
