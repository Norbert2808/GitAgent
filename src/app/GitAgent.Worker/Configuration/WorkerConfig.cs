namespace GitAgent.Worker.Configuration;

public class WorkerConfig
{
    public const string SectionName = "WorkerConfig";

    public string ServerUrl { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public TimeoutConfig Timeouts { get; set; } = new();
}

public class TimeoutConfig
{
    public SignalRTimeoutConfig SignalR { get; set; } = new();
    public int GitProcessTimeoutSeconds { get; set; } = 90;
    public int[] ReconnectDelaysSeconds { get; set; } = [2, 5, 10, 15, 30];
}

public class SignalRTimeoutConfig
{
    public int ServerTimeoutSeconds { get; set; } = 120;
    public int HandshakeTimeoutSeconds { get; set; } = 30;
    public int KeepAliveIntervalSeconds { get; set; } = 15;
}
