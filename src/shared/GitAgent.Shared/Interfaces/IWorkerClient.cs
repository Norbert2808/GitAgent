using GitAgent.Shared.Models;
using GitAgent.Shared.DTOs;

namespace GitAgent.Shared.Interfaces;

public interface IWorkerClient
{
    Task<List<RepositoryInfo>> GetRepositoryConfigs();
    Task<List<BranchInfo>> GetBranches(string repositoryName);
    Task<List<CommitInfo>> GetCommits(GetCommitsRequest request);
    Task<SyncResult> PushBranch(PushBranchRequest request);
}
