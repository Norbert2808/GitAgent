using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Serilog;
using GitAgent.Worker.Configuration;
using GitAgent.Worker.Services;
using GitAgent.Worker.SignalR;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .WriteTo.File("logs/gitagent-worker-.log", rollingInterval: RollingInterval.Day)
    .MinimumLevel.Debug()
    .CreateLogger();

try
{
    Log.Information("🚀 Git Agent Worker starting...");

    var builder = Host.CreateApplicationBuilder(args);

    // Add appsettings.json configuration
    builder.Configuration
        .SetBasePath(AppContext.BaseDirectory)
        .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);

    // Add Serilog
    builder.Services.AddSerilog();

    // Configure options
    builder.Services.Configure<WorkerConfig>(
        builder.Configuration.GetSection(WorkerConfig.SectionName));
    builder.Services.Configure<GitAgentConfig>(
        builder.Configuration.GetSection(GitAgentConfig.SectionName));

    // Register services
    builder.Services.AddSingleton<IProcessExecutor, ProcessExecutor>();
    builder.Services.AddSingleton<IGitAgentService, GitAgentServiceRefactored>();
    builder.Services.AddSingleton<GitServerConnection>(sp =>
    {
        var gitService = sp.GetRequiredService<IGitAgentService>();
        var config = sp.GetRequiredService<IOptions<WorkerConfig>>().Value;
        return new GitServerConnection(config.ServerUrl, config.ApiKey, gitService);
    });

    var host = builder.Build();

    // Start SignalR connection
    var connection = host.Services.GetRequiredService<GitServerConnection>();

    var cts = new CancellationTokenSource();
    Console.CancelKeyPress += (sender, e) =>
    {
        e.Cancel = true;
        cts.Cancel();
        Log.Information("Shutdown requested...");
    };

    await connection.StartAsync(cts.Token);

    var config = host.Services.GetRequiredService<IOptions<WorkerConfig>>().Value;
    Log.Information("✅ Worker is running. Press Ctrl+C to stop.");
    Log.Information($"📡 Connected to: {config.ServerUrl}");

    // Keep alive
    await Task.Delay(Timeout.Infinite, cts.Token);
}
catch (OperationCanceledException)
{
    Log.Information("Worker shutdown completed");
}
catch (Exception ex)
{
    Log.Fatal(ex, "Worker terminated unexpectedly");
    throw;
}
finally
{
    Log.CloseAndFlush();
}
