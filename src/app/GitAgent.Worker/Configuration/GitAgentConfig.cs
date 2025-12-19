namespace GitAgent.Worker.Configuration;

public class GitAgentConfig
{
    public const string SectionName = "GitAgent";

    public RepositoryConfig InternalRepository { get; set; } = new();
    public RepositoryConfig CustomerRepository { get; set; } = new();
}

public class RepositoryConfig
{
    public string Name { get; set; } = string.Empty;
    public string LocalPath { get; set; } = string.Empty;
    public string RemoteUrl { get; set; } = string.Empty;
}
