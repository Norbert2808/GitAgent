using GitAgent.Shared.Models;

namespace GitAgent.Worker.Services;

public interface IGitAgentService
{
    List<RepositoryInfo> GetRepositoryConfigs();
    Task<List<BranchInfo>> GetBranches(string repositoryName);
    Task<List<CommitInfo>> GetBranchCommits(string branchName, string repositoryName, int count = 10);
    Task<SyncResult> PushBranch(string branchName, string fromRepository, string toRepository, bool force = false);
}
