namespace GitAgent.Server.Configuration;

public class ServerConfig
{
    public const string SectionName = "ServerConfig";

    public string WorkerApiKey { get; set; } = string.Empty;
}
