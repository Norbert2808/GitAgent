namespace GitAgent.Server.Configuration;

public class ServerConfig
{
    public const string SectionName = "ServerConfig";

    public string WorkerApiKey { get; set; } = string.Empty;
    public SignalRTimeoutConfig SignalRTimeouts { get; set; } = new();
}

public class SignalRTimeoutConfig
{
    public int ClientTimeoutSeconds { get; set; } = 120;
    public int HandshakeTimeoutSeconds { get; set; } = 30;
    public int KeepAliveIntervalSeconds { get; set; } = 15;
}
