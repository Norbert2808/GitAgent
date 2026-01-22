namespace GitAgent.Server.Configuration;

public class ServerConfig
{
    public const string SectionName = "ServerConfig";

    public string WorkerApiKey { get; set; } = string.Empty;
    public int MaxMessageSizeBytes { get; set; } = 10 * 1024 * 1024; // 10MB default
}
