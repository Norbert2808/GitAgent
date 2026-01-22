using Serilog;
using GitAgent.Server.Services;
using GitAgent.Server.Hubs;
using GitAgent.Server.Configuration;

var builder = WebApplication.CreateBuilder(args);

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .WriteTo.File("logs/gitagent-server-.log", rollingInterval: RollingInterval.Day)
    .MinimumLevel.Debug()
    .CreateLogger();

builder.Host.UseSerilog();

// Configure options
builder.Services.Configure<ServerConfig>(
    builder.Configuration.GetSection(ServerConfig.SectionName));

// Get timeout configuration
var serverConfig = builder.Configuration.GetSection(ServerConfig.SectionName).Get<ServerConfig>() ?? new ServerConfig();

// Add services
builder.Services.AddControllers();
builder.Services.AddHttpClient();

// SignalR for Worker communication
builder.Services.AddSignalR(options =>
{
    options.ClientTimeoutInterval = TimeSpan.FromSeconds(serverConfig.SignalRTimeouts.ClientTimeoutSeconds);
    options.HandshakeTimeout = TimeSpan.FromSeconds(serverConfig.SignalRTimeouts.HandshakeTimeoutSeconds);
    options.KeepAliveInterval = TimeSpan.FromSeconds(serverConfig.SignalRTimeouts.KeepAliveIntervalSeconds);
});

// Worker Service
builder.Services.AddSingleton<IWorkerService, WorkerService>();

var app = builder.Build();

// Serve static files
app.UseDefaultFiles();
app.UseStaticFiles();

app.UseRouting();
app.MapControllers();
app.MapHub<GitWorkerHub>("/workerhub");

// Fallback to index.html for SPA routes
app.MapFallbackToFile("index.html");

Log.Information("🚀 Git Agent Web Server started");
app.Run();
