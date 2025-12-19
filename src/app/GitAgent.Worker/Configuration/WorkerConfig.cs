namespace GitAgent.Worker.Configuration;

public class WorkerConfig
{
    public const string SectionName = "WorkerConfig";

    public string ServerUrl { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
}
