namespace GitAgent.Shared.Models;

public class BranchInfo
{
    public string Name { get; set; } = string.Empty;
    public string LastCommitHash { get; set; } = string.Empty;
    public string LastCommitMessage { get; set; } = string.Empty;
    public string LastCommitAuthor { get; set; } = string.Empty;
    public DateTime LastCommitDate { get; set; }
    public bool IsRemote { get; set; }
    public bool IsSynchronizedWithOther { get; set; }
    public string? OtherRepoCommitHash { get; set; }
}
