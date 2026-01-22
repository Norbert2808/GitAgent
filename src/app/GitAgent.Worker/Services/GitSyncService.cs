using LibGit2Sharp;
using Serilog;
using Microsoft.Extensions.Options;
using GitAgent.Shared.Models;
using GitAgent.Worker.Configuration;

namespace GitAgent.Worker.Services;

public class GitAgentServiceRefactored : IGitAgentService
{
    private readonly GitAgentConfig _config;
    private readonly IProcessExecutor _processExecutor;
    private readonly Dictionary<string, object> _repositoryLocks = new();

    public GitAgentServiceRefactored(IOptions<GitAgentConfig> configuration, IProcessExecutor processExecutor)
    {
        _config = configuration.Value;
        _processExecutor = processExecutor;

        // Initialize locks for each repository
        _repositoryLocks[_config.InternalRepository.Name] = new object();
        _repositoryLocks[_config.CustomerRepository.Name] = new object();
    }

    private object GetRepositoryLock(string repositoryName)
    {
        if (_repositoryLocks.TryGetValue(repositoryName, out var lockObj))
        {
            return lockObj;
        }
        throw new ArgumentException($"Unknown repository: {repositoryName}");
    }

    public List<RepositoryInfo> GetRepositoryConfigs()
    {
        return
        [
            new RepositoryInfo { Name = _config.InternalRepository.Name },
            new RepositoryInfo { Name = _config.CustomerRepository.Name }
        ];
    }

    public async Task<List<BranchInfo>> GetBranches(string repositoryName)
    {
        var (repoConfig, otherRepoConfig) = GetRepositoryConfigs(repositoryName);
        return await GetBranchesWithSync(repoConfig, otherRepoConfig);
    }

    private (RepositoryConfig repo, RepositoryConfig other) GetRepositoryConfigs(string repositoryName)
    {
        if (repositoryName == _config.InternalRepository.Name)
        {
            return (_config.InternalRepository, _config.CustomerRepository);
        }

        if (repositoryName == _config.CustomerRepository.Name)
        {
            return (_config.CustomerRepository, _config.InternalRepository);
        }

        throw new ArgumentException($"Unknown repository: {repositoryName}");
    }

    private async Task<List<BranchInfo>> GetBranchesWithSync(RepositoryConfig repoConfig, RepositoryConfig otherRepoConfig)
    {
        return await Task.Run(() =>
        {
            // Lock both repositories in consistent order to prevent deadlock
            var locks = new[] { repoConfig.Name, otherRepoConfig.Name }
                .OrderBy(name => name)
                .Select(name => GetRepositoryLock(name))
                .ToArray();

            lock (locks[0])
            {
                lock (locks[1])
                {
                    try
                    {
                        Log.Information($"Getting branches for {repoConfig.Name} with sync to {otherRepoConfig.Name}");

                        EnsureRepositoryCloned(repoConfig);
                        EnsureRepositoryCloned(otherRepoConfig);

                        using var repo = new Repository(repoConfig.LocalPath);
                        using var otherRepo = new Repository(otherRepoConfig.LocalPath);

                        FetchRemote(repoConfig.LocalPath);
                        FetchRemote(otherRepoConfig.LocalPath);

                        var branches = new List<BranchInfo>();

                        foreach (var branch in repo.Branches.Where(b => b.IsRemote && b.FriendlyName.StartsWith("origin/")))
                        {
                            try
                            {
                                var branchName = branch.FriendlyName.Replace("origin/", "");
                                if (branchName == "HEAD") continue;

                                var lastCommit = branch.Tip;
                                var otherBranch = otherRepo.Branches[$"origin/{branchName}"];
                                var isSynchronized = otherBranch != null && otherBranch.Tip.Sha == lastCommit.Sha;
                                var otherCommitHash = otherBranch?.Tip.Sha;

                                branches.Add(new BranchInfo
                                {
                                    Name = branchName,
                                    LastCommitHash = lastCommit.Sha,
                                    LastCommitMessage = lastCommit.MessageShort,
                                    LastCommitAuthor = lastCommit.Author.Name,
                                    LastCommitDate = lastCommit.Author.When.DateTime,
                                    IsRemote = true,
                                    IsSynchronizedWithOther = isSynchronized,
                                    OtherRepoCommitHash = otherCommitHash
                                });
                            }
                            catch (Exception ex)
                            {
                                Log.Warning(ex, $"Error processing branch {branch.FriendlyName}, skipping");
                            }
                        }

                        Log.Information($"Successfully retrieved {branches.Count} branches for {repoConfig.Name}");
                        return branches.OrderByDescending(b => b.LastCommitDate).ToList();
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, $"Error getting branches for {repoConfig.Name}");
                        throw;
                    }
                }
            }
        });
    }

    public async Task<List<CommitInfo>> GetBranchCommits(string branchName, string repositoryName, int count = 10)
    {
        return await Task.Run(() =>
        {
            var (repoConfig, _) = GetRepositoryConfigs(repositoryName);
            var repoLock = GetRepositoryLock(repositoryName);

            lock (repoLock)
            {
                EnsureRepositoryCloned(repoConfig);

                using var repo = new Repository(repoConfig.LocalPath);
                FetchRemote(repoConfig.LocalPath);

                var branch = repo.Branches[$"origin/{branchName}"];
                if (branch == null)
                {
                    Log.Warning($"Branch {branchName} not found in {repositoryName}");
                    return [];
                }

                var commits = new List<CommitInfo>();
                var commitCount = 0;

                foreach (var commit in branch.Commits)
                {
                    if (commitCount >= count) break;

                    commits.Add(new CommitInfo
                    {
                        Hash = commit.Sha,
                        ShortHash = commit.Sha[..8],
                        Message = commit.MessageShort,
                        Author = commit.Author.Name,
                        Date = commit.Author.When.DateTime
                    });

                    commitCount++;
                }

                return commits;
            }
        });
    }

    public async Task<SyncResult> PushBranch(string branchName, string fromRepository, string toRepository, bool force = false)
    {
        var (fromConfig, _) = GetRepositoryConfigs(fromRepository);
        var (toConfig, _) = GetRepositoryConfigs(toRepository);

        return await PushBranchInternal(fromConfig, toConfig, branchName, force);
    }

    private async Task<SyncResult> PushBranchInternal(
        RepositoryConfig fromConfig,
        RepositoryConfig toConfig,
        string branchName,
        bool force)
    {
        return await Task.Run(() =>
        {
            try
            {
                EnsureRepositoryCloned(fromConfig);
                EnsureRepositoryCloned(toConfig);

                using var fromRepository = new Repository(fromConfig.LocalPath);
                using var toRepository = new Repository(toConfig.LocalPath);

                FetchRemote(fromConfig.LocalPath);
                FetchRemote(toConfig.LocalPath);

                var fromBranch = fromRepository.Branches[$"origin/{branchName}"];
                var toBranch = toRepository.Branches[$"origin/{branchName}"];

                if (fromBranch == null)
                {
                    return new SyncResult
                    {
                        Success = false,
                        Message = $"❌ Branch '{branchName}' not found in {fromConfig.Name}",
                        HasConflicts = false
                    };
                }

                // Check if already synchronized
                if (toBranch != null && fromBranch.Tip.Sha == toBranch.Tip.Sha)
                {
                    return new SyncResult
                    {
                        Success = true,
                        Message = $"✅ Branch '{branchName}' is already synchronized\nNo push needed",
                        HasConflicts = false
                    };
                }

                // Check if fast-forward is possible (toBranch is ancestor of fromBranch)
                bool canFastForward = false;
                if (toBranch != null && !force)
                {
                    canFastForward = IsAncestor(fromConfig.LocalPath, toBranch.Tip.Sha, fromBranch.Tip.Sha);

                    if (!canFastForward)
                    {
                        // Branches have diverged - need force push
                        return new SyncResult
                        {
                            Success = false,
                            HasConflicts = true,
                            Message = $"⚠️ Branches have diverged!\nFrom: {fromBranch.Tip.Sha.Substring(0, 8)} - {fromBranch.Tip.MessageShort}\nTo: {toBranch.Tip.Sha.Substring(0, 8)} - {toBranch.Tip.MessageShort}\n\nUse Force Push to overwrite.",
                            ConflictDetails = new List<string>
                            {
                                $"Source: {fromBranch.Tip.MessageShort}",
                                $"Target: {toBranch.Tip.MessageShort}"
                            }
                        };
                    }
                }

                // Perform push
                Log.Information($"Pushing {branchName} from {fromConfig.Name} to {toConfig.Name} (fast-forward: {canFastForward}, force: {force})");

                // Checkout branch locally
                var checkoutResult = _processExecutor.Execute("git", $"checkout -B {branchName} origin/{branchName}", fromConfig.LocalPath);
                if (!checkoutResult.Success)
                {
                    return new SyncResult
                    {
                        Success = false,
                        Message = $"❌ Failed to checkout branch: {checkoutResult.Error}",
                        HasConflicts = false
                    };
                }

                // Add temporary remote
                var tempRemoteName = $"temp_{Guid.NewGuid().ToString("N").Substring(0, 8)}";
                _ = _processExecutor.Execute("git", $"remote add {tempRemoteName} {toConfig.RemoteUrl}", fromConfig.LocalPath);

                try
                {
                    // Push
                    var pushArgs = force ? $"push {tempRemoteName} {branchName} --force" : $"push {tempRemoteName} {branchName}";
                    var pushResult = _processExecutor.Execute("git", pushArgs, fromConfig.LocalPath);

                    if (!pushResult.Success)
                    {
                        Log.Error($"Git push failed: {pushResult.Error}");

                        // Check if this is a "target has more commits" scenario
                        if ((pushResult.Error.Contains("rejected") || pushResult.Error.Contains("fetch first")) && toBranch != null)
                        {
                            // Check if the reverse is true (fromBranch is ancestor of toBranch)
                            var targetIsAhead = IsAncestor(fromConfig.LocalPath, fromBranch.Tip.Sha, toBranch.Tip.Sha);

                            if (targetIsAhead)
                            {
                                return new SyncResult
                                {
                                    Success = false,
                                    HasConflicts = true,
                                    Message = $"⚠️ Cannot push - target repository has additional commits!\n\n" +
                                              $"Source ({fromConfig.Name}): {fromBranch.Tip.Sha.Substring(0, 8)} - {fromBranch.Tip.MessageShort}\n" +
                                              $"Target ({toConfig.Name}): {toBranch.Tip.Sha.Substring(0, 8)} - {toBranch.Tip.MessageShort}\n\n" +
                                              $"⚠️ WARNING: Using Force Push will delete commits from {toConfig.Name}!",
                                    ConflictDetails =
                                    [
                                        "Target has commits not in source",
                                        "Force push required but will cause data loss"
                                    ]
                                };
                            }
                        }

                        return new SyncResult
                        {
                            Success = false,
                            Message = $"❌ Push failed: {pushResult.Error}",
                            HasConflicts = false
                        };
                    }

                    Log.Information($"Successfully pushed {branchName} from {fromConfig.Name} to {toConfig.Name}");

                    var messageType = force ? "⚠️ Force push" : (canFastForward ? "Fast-forward" : "Push");
                    return new SyncResult
                    {
                        Success = true,
                        Message = $"✅ Successfully synchronized branch '{branchName}'\nFrom: {fromConfig.Name}\nTo: {toConfig.Name}\nType: {messageType}",
                        HasConflicts = false
                    };
                }
                finally
                {
                    // Remove temporary remote
                    _processExecutor.Execute("git", $"remote remove {tempRemoteName}", fromConfig.LocalPath);
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, $"Error pushing branch {branchName}");
                return new SyncResult
                {
                    Success = false,
                    Message = $"❌ Synchronization error: {ex.Message}",
                    HasConflicts = false
                };
            }
        });
    }

    private bool IsAncestor(string repoPath, string ancestorSha, string descendantSha)
    {
        try
        {
            // Use git merge-base --is-ancestor to check if ancestorSha is an ancestor of descendantSha
            var result = _processExecutor.Execute("git", $"merge-base --is-ancestor {ancestorSha} {descendantSha}", repoPath);

            if (result.ExitCode == 0)
            {
                Log.Debug($"{ancestorSha.Substring(0, 8)} is an ancestor of {descendantSha.Substring(0, 8)}");
                return true;
            }

            if (result.ExitCode == 1)
            {
                Log.Debug($"{ancestorSha.Substring(0, 8)} is NOT an ancestor of {descendantSha.Substring(0, 8)}");
                return false;
            }

            Log.Warning($"git merge-base returned unexpected exit code {result.ExitCode}: {result.Error}");
            return false;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, $"Error checking if {ancestorSha} is ancestor of {descendantSha}");
            return false;
        }
    }

    private void EnsureRepositoryCloned(RepositoryConfig config)
    {
        if (Directory.Exists(config.LocalPath) && Repository.IsValid(config.LocalPath))
        {
            // Repository already exists and is valid
            return;
        }

        Log.Warning($"Repository {config.Name} not found at {config.LocalPath}");
        Log.Information("Attempting to clone repository automatically...");

        try
        {
            // Create parent directory if it doesn't exist
            var parentDir = Path.GetDirectoryName(config.LocalPath);
            if (!string.IsNullOrEmpty(parentDir) && !Directory.Exists(parentDir))
            {
                Directory.CreateDirectory(parentDir);
                Log.Information($"Created directory: {parentDir}");
            }

            // Try to clone the repository
            Log.Information($"Cloning {config.RemoteUrl} to {config.LocalPath}...");
            var result = _processExecutor.Execute("git", $"clone {config.RemoteUrl} {config.LocalPath}", parentDir ?? Directory.GetCurrentDirectory());

            if (result.Success)
            {
                Log.Information($"✅ Successfully cloned {config.Name}");
                return;
            }

            Log.Error($"Failed to clone repository: {result.Error}");
            throw new InvalidOperationException(
                $"Failed to automatically clone {config.Name}.\n" +
                $"Error: {result.Error}\n\n" +
                $"Please clone manually:\n" +
                $"git clone {config.RemoteUrl} {config.LocalPath}");
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            Log.Error(ex, $"Error while trying to clone repository {config.Name}");
            throw new InvalidOperationException(
                $"Failed to automatically clone {config.Name}.\n" +
                $"Error: {ex.Message}\n\n" +
                $"Please clone manually:\n" +
                $"git clone {config.RemoteUrl} {config.LocalPath}");
        }
    }

    private void FetchRemote(string repoPath)
    {
        try
        {
            var result = _processExecutor.Execute("git", "fetch origin --prune", repoPath);
            if (!result.Success)
            {
                Log.Warning($"Git fetch warning: {result.Error}");
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to fetch remote updates, using cached data");
        }
    }
}
