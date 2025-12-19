using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;
using Serilog;
using GitAgent.Shared;
using GitAgent.Shared.Interfaces;
using GitAgent.Server.Configuration;

namespace GitAgent.Server.Hubs;

public class GitWorkerHub : Hub<IWorkerClient>
{
    private readonly ServerConfig _config;
    private static string? _workerConnectionId;

    public GitWorkerHub(IOptions<ServerConfig> configuration)
    {
        _config = configuration.Value;
    }

    public override async Task OnConnectedAsync()
    {
        var apiKey = Context.GetHttpContext()?.Request.Headers[Constants.ApiKeyHeaderName].FirstOrDefault();
        var expectedApiKey = _config.WorkerApiKey;

        Log.Information("Worker connecting...");

        if (string.IsNullOrEmpty(apiKey) || apiKey != expectedApiKey)
        {
            Log.Warning("Worker authentication failed. Invalid API key.");
            Context.Abort();
            return;
        }

        _workerConnectionId = Context.ConnectionId;
        Log.Information($"✅ Worker connected. Connection ID: {Context.ConnectionId}");

        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        if (_workerConnectionId == Context.ConnectionId)
        {
            _workerConnectionId = null;
            Log.Warning($"Worker disconnected. Connection ID: {Context.ConnectionId}. Error: {exception?.Message ?? "None"}");
        }

        await base.OnDisconnectedAsync(exception);
    }

    public static string? WorkerConnectionId => _workerConnectionId;
    public static bool IsWorkerConnected => !string.IsNullOrEmpty(_workerConnectionId);
}
