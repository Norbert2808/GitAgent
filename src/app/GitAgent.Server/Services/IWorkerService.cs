using GitAgent.Shared.Models;
using GitAgent.Shared.DTOs;

namespace GitAgent.Server.Services;

public interface IWorkerService
{
    Task<List<RepositoryInfo>> GetRepositoryConfigs();
    Task<List<BranchInfo>> GetBranches(string repositoryName);
    Task<List<CommitInfo>> GetBranchCommits(string branchName, string repositoryName, int count = 10);
    Task<SyncResult> PushBranch(PushBranchRequest request);
    bool IsWorkerConnected();
}
