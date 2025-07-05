using CherryPickSmart.Models;
using LibGit2Sharp;
using Spectre.Console;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.RegularExpressions;
using Tree = LibGit2Sharp.Tree;

namespace CherryPickSmart.Core.GitAnalysis;

/// <summary>
/// Detects commits that would result in empty cherry-picks
/// </summary>
public class EmptyCommitDetector(string repositoryPath, EmptyCommitDetectorOptions? options = null)
    : IDisposable
{
    private readonly EmptyCommitDetectorOptions _options = options ?? new EmptyCommitDetectorOptions();
    private Repository? _repo;
    private Dictionary<string, (HashSet<string> commits, Tree tree)>? _branchCache;
    private static readonly Regex RevertCommitRegex = new Regex(@"This reverts commit ([a-f0-9]{40})",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Detects which commits would be empty when cherry-picked to the target branch
    /// </summary>
    public Dictionary<string, EmptyCommitInfo> DetectEmptyCommits(
        List<CpCommit> commits,
        string targetBranch)
    {
        var result = DetectEmptyCommitsWithDetails(commits, targetBranch);
        return result.EmptyCommits;
    }

    /// <summary>
    /// Detects empty commits with detailed results and statistics
    /// </summary>
    public EmptyCommitDetectionResult DetectEmptyCommitsWithDetails(
        List<CpCommit> commits,
        string targetBranch,
        Action<string>? statusCallback = null)
    {
        var stopwatch = Stopwatch.StartNew();
        var result = new EmptyCommitDetectionResult();
        var processed = 0;
        var total = commits.Count;

        using var repo = GetRepository();
        var targetBranchRef = repo.Branches[targetBranch];

        if (targetBranchRef == null)
        {
            throw new ArgumentException($"Target branch '{targetBranch}' not found");
        }

        // Pre-cache branch information for performance
        var (targetCommitShas, targetTree) = GetBranchInfo(targetBranch);

        foreach (var commit in commits)
        {
            processed++;
            statusCallback?.Invoke($"🔍 Checking commit {processed}/{total} for emptiness...");

            try
            {
                var emptyInfo = CheckIfCommitWouldBeEmpty(repo, commit, targetBranchRef, targetCommitShas, targetTree);
                if (emptyInfo != null)
                {
                    result.EmptyCommits[commit.Sha] = emptyInfo;

                    // Update reason counts
                    result.ReasonCounts.TryGetValue(emptyInfo.Reason, out var count);
                    result.ReasonCounts[emptyInfo.Reason] = count + 1;

                    // Check early exit conditions
                    if (_options.StopOnFirstEmpty ||
                        (_options.MaxEmptyCommitsToDetect.HasValue &&
                         result.EmptyCommits.Count >= _options.MaxEmptyCommitsToDetect.Value))
                    {
                        result.WarningMessages.Add($"Stopped early after detecting {result.EmptyCommits.Count} empty commits");
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                if (_options.EnableDetailedLogging)
                {
                    result.WarningMessages.Add($"Failed to analyze commit {commit.Sha}: {ex.Message}");
                }
                // Continue with other commits
            }
        }

        result.DetectionTime = stopwatch.Elapsed;
        result.TotalCommitsAnalyzed = processed;
        return result;
    }

    /// <summary>
    /// Async version with progress reporting
    /// </summary>
    public async Task<Dictionary<string, EmptyCommitInfo>> DetectEmptyCommitsAsync(
        List<CpCommit> commits,
        string targetBranch,
        Action<string>? statusCallback = null,
        CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
            var result = new Dictionary<string, EmptyCommitInfo>();
            var processed = 0;
            var total = commits.Count;

            using var repo = new Repository(repositoryPath);
            var targetBranchRef = repo.Branches[targetBranch];

            if (targetBranchRef == null)
            {
                throw new ArgumentException($"Target branch '{targetBranch}' not found");
            }

            var (targetCommitShas, targetTree) = GetBranchInfo(targetBranch);

            foreach (var commit in commits)
            {
                cancellationToken.ThrowIfCancellationRequested();

                processed++;
                statusCallback?.Invoke($"🔍 Checking commit {processed}/{total} for emptiness...");

                var emptyInfo = CheckIfCommitWouldBeEmpty(repo, commit, targetBranchRef, targetCommitShas, targetTree);
                if (emptyInfo != null)
                {
                    result[commit.Sha] = emptyInfo;
                }
            }

            return result;
        }, cancellationToken);
    }

    /// <summary>
    /// Parallel version for better performance with large commit lists
    /// </summary>
    public async Task<EmptyCommitDetectionResult> DetectEmptyCommitsParallelAsync(
        List<CpCommit> commits,
        string targetBranch,
        IProgress<DetectionProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var result = new EmptyCommitDetectionResult();
        var emptyCommits = new ConcurrentDictionary<string, EmptyCommitInfo>();
        var processedCount = 0;
        var totalCount = commits.Count;

        // Pre-fetch branch info once
        using var repo = new Repository(repositoryPath);
        var targetBranchRef = repo.Branches[targetBranch];
        if (targetBranchRef == null)
            throw new ArgumentException($"Target branch '{targetBranch}' not found");

        var targetCommitShas = GetTargetBranchCommits(repo, targetBranchRef);
        var targetTreeSha = targetBranchRef.Tip.Tree.Sha;

        await Parallel.ForEachAsync(
            commits,
            new ParallelOptions
            {
                MaxDegreeOfParallelism = _options.MaxDegreeOfParallelism,
                CancellationToken = cancellationToken
            }, (commit, ct) =>
            {
                // Each task gets its own repo instance
                using var taskRepo = new Repository(repositoryPath);
                var taskTargetBranch = taskRepo.Branches[targetBranch];
               // var taskTargetTree = taskRepo.Lookup<Tree>(targetTreeSha);
               var taskTargetTree = taskTargetBranch.Tip.Tree;

                try
                {
                    var emptyInfo = CheckIfCommitWouldBeEmpty(
                        taskRepo, commit, taskTargetBranch!, targetCommitShas, taskTargetTree!);

                    if (emptyInfo != null)
                    {
                        emptyCommits.TryAdd(commit.Sha, emptyInfo);
                    }
                }
                catch (Exception ex)
                {
                    if (_options.EnableDetailedLogging)
                    {
                        lock (result.WarningMessages)
                        {
                            result.WarningMessages.Add($"Failed to analyze commit {commit.Sha}: {ex.Message}");
                        }
                    }
                }

                var current = Interlocked.Increment(ref processedCount);
                progress?.Report(new DetectionProgress
                {
                    ProcessedCommits = current,
                    EmptyCommits = emptyCommits.Count,
                    TotalCommits = totalCount,
                    CurrentCommit = commit.Sha,
                    PercentComplete = (current * 100.0) / totalCount
                });
                return ValueTask.CompletedTask;
            });

        result.EmptyCommits = new Dictionary<string, EmptyCommitInfo>(emptyCommits);
        result.TotalCommitsAnalyzed = commits.Count;
        result.DetectionTime = stopwatch.Elapsed;

        // Calculate reason counts
        foreach (var emptyInfo in result.EmptyCommits.Values)
        {
            result.ReasonCounts.TryGetValue(emptyInfo.Reason, out var count);
            result.ReasonCounts[emptyInfo.Reason] = count + 1;
        }

        return result;
    }

    private Repository GetRepository()
    {
        return _repo ??= new Repository(repositoryPath);
    }

    private (HashSet<string> commits, Tree tree) GetBranchInfo(string branchName)
    {
        _branchCache ??= new Dictionary<string, (HashSet<string>, Tree)>();

        if (!_branchCache.ContainsKey(branchName))
        {
            var repo = GetRepository();
            var branch = repo.Branches[branchName];
            if (branch == null)
                throw new ArgumentException($"Branch '{branchName}' not found");

            var commits = GetTargetBranchCommits(repo, branch);
            _branchCache[branchName] = (commits, branch.Tip.Tree);
        }

        return _branchCache[branchName];
    }

    private EmptyCommitInfo? CheckIfCommitWouldBeEmpty(
        Repository repo,
        CpCommit cpCommit,
        Branch targetBranch,
        HashSet<string> targetCommitShas,
        Tree targetTree)
    {
        try
        {
            // Quick check: if commit is already in target
            if (targetCommitShas.Contains(cpCommit.Sha))
            {
                return new EmptyCommitInfo
                {
                    CommitSha = cpCommit.Sha,
                    Reason = EmptyReason.ChangesAlreadyApplied,
                    Details = "Commit already exists in target branch"
                };
            }

            var commit = repo.Lookup<Commit>(cpCommit.Sha);
            if (commit == null) return null;
            
            // For merge commits, check if all changes are already in target
            if (cpCommit.IsMergeCommit)
            {
                return CheckMergeCommitEmpty(repo, commit, targetBranch, targetCommitShas, targetTree);
            }

            // For regular commits, check if the exact changes exist
            return CheckRegularCommitEmpty(repo, commit, targetBranch, targetCommitShas, targetTree);
        }
        catch (LibGit2SharpException ex)
        {
            // If we can't analyze the commit, assume it's not empty
            if (_options.EnableDetailedLogging)
            {
                throw new EmptyCommitDetectionException(
                    $"Failed to analyze commit: {ex.Message}",
                    cpCommit.Sha,
                    targetBranch.FriendlyName,
                    "CheckIfCommitWouldBeEmpty",
                    ex);
            }
            return null;
        }
    }

    private EmptyCommitInfo? CheckRegularCommitEmpty(
        Repository repo,
        Commit commit,
        Branch targetBranch,
        HashSet<string> targetCommitShas,
        Tree targetTree)
    {
        if (!commit.Parents.Any())
        {
            // Root commit - check if all files already exist with same content
            return CheckRootCommitEmpty(repo, commit, targetBranch);
        }

        var sourceChanges = repo.Diff.Compare<TreeChanges>(commit.Tree, targetTree);

        // Check if all changes from this commit already exist in target
        var allChangesFound = true;
        var reasonParts = new List<string>();

        foreach (var change in sourceChanges)
        {
            var isChangeInTarget = IsChangeAlreadyInTarget(repo, change, commit, targetBranch.Tip);

            if (!isChangeInTarget)
            {
                allChangesFound = false;
                break;
            }

            reasonParts.Add($"{change.Path} already has these changes");
        }

        if (allChangesFound)
        {
            return new EmptyCommitInfo
            {
                CommitSha = commit.Sha,
                Reason = EmptyReason.ChangesAlreadyApplied,
                Details = $"All changes already exist in target branch: {string.Join(", ", reasonParts.Take(3))}" +
                         (reasonParts.Count > 3 ? $" and {reasonParts.Count - 3} more files" : "")
            };
        }

        // Check if this is a revert that undoes changes not in target
        if (IsRevertOfNonExistentChanges(repo, commit, targetBranch, targetCommitShas))
        {
            return new EmptyCommitInfo
            {
                CommitSha = commit.Sha,
                Reason = EmptyReason.RevertOfNonExistentChanges,
                Details = "This is a revert of changes that don't exist in the target branch"
            };
        }

        return null;
    }

    private EmptyCommitInfo? CheckMergeCommitEmpty(
        Repository repo,
        Commit mergeCommit,
        Branch targetBranch,
        HashSet<string> targetCommitShas,
        Tree targetTree)
    {
        // Quick check: if merge commit is already in target
        if (targetCommitShas.Contains(mergeCommit.Sha))
        {
            return new EmptyCommitInfo
            {
                CommitSha = mergeCommit.Sha,
                Reason = EmptyReason.ChangesAlreadyApplied,
                Details = "Merge commit already exists in target branch"
            };
        }

        // For merge commits, we need to check if the merged changes are already in target
        if (mergeCommit.Parents.Count() < 2)
        {
            return null; // Not a merge commit
        }

        // Check if all parent commits are already in target
        var parentsInTarget = mergeCommit.Parents
            .Count(p => targetCommitShas.Contains(p.Sha));

        if (parentsInTarget == mergeCommit.Parents.Count())
        {
            return new EmptyCommitInfo
            {
                CommitSha = mergeCommit.Sha,
                Reason = EmptyReason.MergedChangesAlreadyPresent,
                Details = "All parent commits already exist in target branch"
            };
        }

        var changes = repo.Diff.Compare<TreeChanges>(mergeCommit.Tree, targetTree);
        if (changes.Count == 0)
        {
            // This is a no-op merge (no actual changes)
            return new EmptyCommitInfo
            {
                CommitSha = mergeCommit.Sha,
                Reason = EmptyReason.NoOpMerge,
                Details = "Merge commit contains no actual file changes"
            };
        }

        // Check if all merged changes already exist in target
        var allChangesInTarget = true;

        var conflicts = new List<string>();
        foreach (var change in changes)
        {
            if (!IsChangeAlreadyInTarget(repo, change, mergeCommit, targetBranch.Tip))
            {
                allChangesInTarget = false;
                conflicts.Add(change.Path);
            }
        }

        if (allChangesInTarget)
        {
            return new EmptyCommitInfo
            {
                CommitSha = mergeCommit.Sha,
                Reason = EmptyReason.MergedChangesAlreadyPresent,
                Details = "All changes from this merge already exist in the target branch"
            };
        }

        return null;
    }

    private static bool IsValidFile(string changePath)
    {
        return !changePath.EndsWith("packages.lock.json");
    }

    private EmptyCommitInfo? CheckRootCommitEmpty(
        Repository repo,
        Commit rootCommit,
        Branch targetBranch)
    {
        // For root commits, check if all files already exist with the same content
        var allFilesExist = true;

        foreach (var entry in rootCommit.Tree)
        {
            var targetEntry = targetBranch.Tip[entry.Path];
            if (targetEntry == null || targetEntry.Target.Sha != entry.Target.Sha)
            {
                allFilesExist = false;
                break;
            }
        }

        if (allFilesExist)
        {
            return new EmptyCommitInfo
            {
                CommitSha = rootCommit.Sha,
                Reason = EmptyReason.ChangesAlreadyApplied,
                Details = "All files from root commit already exist in target with same content"
            };
        }

        return null;
    }

    private bool IsChangeAlreadyInTarget(
        Repository repo,
        TreeEntryChanges change,
        Commit sourceCommit,
        Commit targetCommit)
    {
        try
        {
            if (!IsValidFile(change.Path))
            {
                return true;
            }
            
            
            switch (change.Status)
            {
                case ChangeKind.Deleted:
                    return targetCommit[change.Path] == null;

                case ChangeKind.Renamed:
                    // Check both old and new paths
                    var sourceEntry = sourceCommit[change.Path];
                    var targetEntryNewPath = targetCommit[change.Path];
                    var targetEntryOldPath = targetCommit[change.OldPath];

                    // File should not exist at old path and should exist at new path with same content
                    return targetEntryOldPath == null &&
                           targetEntryNewPath != null &&
                           sourceEntry?.Target.Sha == targetEntryNewPath.Target.Sha;

                case ChangeKind.Added:
                case ChangeKind.Modified:
                    var source = sourceCommit[change.Path];
                    var target = targetCommit[change.Path];

                    if (source == null || target == null)
                        return false;

                    // Handle whitespace comparison if configured
                    if (_options.IgnoreWhitespaceChanges && !IsBinaryFile(repo, source))
                    {
                        return CompareTextFilesIgnoringWhitespace(repo, source, target);
                    }

                    // For binary files or when not ignoring whitespace, compare SHA
                    return source.Target.Sha == target.Target.Sha;

                case ChangeKind.TypeChanged:
                    // Handle cases where file type changed (e.g., file to symlink)
                    return false;

                default:
                    return false;
            }
        }
        catch (Exception ex)
        {
            if (_options.EnableDetailedLogging)
            {
                // Log the exception for debugging
                Console.WriteLine($"Error checking change for {change.Path}: {ex.Message}");
            }
            return false;
        }
    }

    private bool IsBinaryFile(Repository repo, TreeEntry entry)
    {
        if (entry.TargetType != TreeEntryTargetType.Blob)
            return false;

        var blob = (Blob)entry.Target;
        return blob.IsBinary;
    }

    private bool CompareTextFilesIgnoringWhitespace(Repository repo, TreeEntry source, TreeEntry target)
    {
        try
        {
            var sourceBlob = (Blob)source.Target;
            var targetBlob = (Blob)target.Target;

            var sourceContent = sourceBlob.GetContentText()
                .Replace("\r\n", "\n")
                .Replace("\r", "\n")
                .Trim();

            var targetContent = targetBlob.GetContentText()
                .Replace("\r\n", "\n")
                .Replace("\r", "\n")
                .Trim();

            return sourceContent == targetContent;
        }
        catch
        {
            // If we can't compare as text, fall back to SHA comparison
            return source.Target.Sha == target.Target.Sha;
        }
    }

    private bool IsRevertOfNonExistentChanges(
        Repository repo,
        Commit commit,
        Branch targetBranch,
        HashSet<string> targetCommitShas)
    {
        // Check if commit message indicates a revert
        if (!commit.Message.StartsWith("Revert", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // Extract the reverted commit SHA from the message if possible
        var revertedShaMatch = RevertCommitRegex.Match(commit.Message);

        if (!revertedShaMatch.Success)
        {
            return false;
        }

        var revertedSha = revertedShaMatch.Groups[1].Value;

        // Check if the reverted commit exists in target branch
        return !targetCommitShas.Contains(revertedSha);
    }

    private HashSet<string> GetTargetBranchCommits(Repository repo, Branch branch)
    {
        var filter = new CommitFilter
        {
            IncludeReachableFrom = branch,
            SortBy = CommitSortStrategies.Time
        };

        return repo.Commits.QueryBy(filter).Select(c => c.Sha).ToHashSet();
    }

    /// <summary>
    /// Checks if a specific commit is effectively empty by attempting a dry-run merge
    /// This is more accurate but slower than the heuristic-based detection
    /// </summary>
    public bool IsCommitEffectivelyEmpty(string commitSha, string targetBranch)
    {
        using var repo = new Repository(repositoryPath);

        try
        {
            var commit = repo.Lookup<Commit>(commitSha);
            var target = repo.Branches[targetBranch];

            if (commit == null || target == null)
            {
                return false;
            }

            // Create a temporary branch for testing
            var tempBranchName = $"temp-empty-check-{Guid.NewGuid():N}";
            var tempBranch = repo.CreateBranch(tempBranchName, target.Tip);

            try
            {
                LibGit2Sharp.Commands.Checkout(repo, tempBranch);

                // Try to cherry-pick
                var options = new CherryPickOptions
                {
                    MergeFileFavor = MergeFileFavor.Ours,
                    IgnoreWhitespaceChange = true,
                   
                };
                
                var result = repo.CherryPick(commit, commit.Committer, options);

                // Check if the result would be empty
                var status = repo.RetrieveStatus();
                var isEmpty = result.Status == CherryPickStatus.CherryPicked &&
                             !status.Any();

                return isEmpty;
            }
            finally
            {
                // Clean up
                LibGit2Sharp.Commands.Checkout(repo, target);
                repo.Branches.Remove(tempBranch);
            }
        }
        catch
        {
            // If we can't determine, assume not empty
            return false;
        }
    }

    public void Dispose()
    {
        _repo?.Dispose();
    }
}

// Supporting classes

public class EmptyCommitInfo
{
    public string CommitSha { get; set; } = "";
    public EmptyReason Reason { get; set; }
    public string Details { get; set; } = "";
}

public enum EmptyReason
{
    Unknown,
    ChangesAlreadyApplied,
    NoOpMerge,
    MergedChangesAlreadyPresent,
    RevertOfNonExistentChanges,
    ConflictResolutionOnly
}

public class EmptyCommitDetectorOptions
{
    public bool IgnoreWhitespaceChanges { get; set; } = true;
    public bool ConsiderModeChanges { get; set; } = true;
    public bool UseParallelProcessing { get; set; } = true;
    public int MaxDegreeOfParallelism { get; set; } = 4;
    public bool EnableDetailedLogging { get; set; }
    public TimeSpan Timeout { get; set; } = TimeSpan.FromMinutes(5);
    public int? MaxEmptyCommitsToDetect { get; set; }
    public bool StopOnFirstEmpty { get; set; }
}

public class EmptyCommitDetectionResult
{
    public Dictionary<string, EmptyCommitInfo> EmptyCommits { get; set; } = new();
    public Dictionary<EmptyReason, int> ReasonCounts { get; set; } = new();
    public List<string> WarningMessages { get; set; } = [];
    public TimeSpan DetectionTime { get; set; }
    public int TotalCommitsAnalyzed { get; set; }

    public string GetSummaryMessage()
    {
        if (EmptyCommits.Count == 0)
            return "No empty commits detected";

        var parts = new List<string>();
        foreach (var (reason, count) in ReasonCounts.OrderByDescending(r => r.Value))
        {
            parts.Add($"{count} {reason}");
        }

        return $"Found {EmptyCommits.Count} empty commits: {string.Join(", ", parts)}";
    }
}

public class DetectionProgress
{
    public int ProcessedCommits { get; set; }
    public int TotalCommits { get; set; }
    public string CurrentCommit { get; set; } = "";
    public double PercentComplete { get; set; }
    public int EmptyCommits { get; set; }
}

public class EmptyCommitDetectionException : Exception
{
    public string CommitSha { get; set; }
    public string TargetBranch { get; set; }
    public string Phase { get; set; }

    public EmptyCommitDetectionException(string message, string commitSha, string targetBranch, string phase, Exception? innerException = null)
        : base($"Error detecting empty commit {commitSha} for branch {targetBranch} during {phase}: {message}", innerException)
    {
        CommitSha = commitSha;
        TargetBranch = targetBranch;
        Phase = phase;
    }
}

// Extension methods

public static class EmptyCommitInfoExtensions
{
    public static string GetUserFriendlyDescription(this EmptyCommitInfo info)
    {
        return info.Reason switch
        {
            EmptyReason.ChangesAlreadyApplied => "✓ Changes already in target",
            EmptyReason.NoOpMerge => "⊘ No-op merge (no changes)",
            EmptyReason.MergedChangesAlreadyPresent => "🔀 Merge already applied",
            EmptyReason.RevertOfNonExistentChanges => "↩ Reverting non-existent changes",
            EmptyReason.ConflictResolutionOnly => "🤝 Conflict resolution only",
            _ => "? Unknown reason"
        };
    }

    public static Color GetDisplayColor(this EmptyCommitInfo info)
    {
        return info.Reason switch
        {
            EmptyReason.ChangesAlreadyApplied => Color.Green,
            EmptyReason.NoOpMerge => Color.Grey,
            EmptyReason.MergedChangesAlreadyPresent => Color.Blue,
            EmptyReason.RevertOfNonExistentChanges => Color.Yellow,
            EmptyReason.ConflictResolutionOnly => Color.Orange1,
            _ => Color.Grey100
        };
    }

    public static string GetGitCommand(this EmptyCommitInfo info, string originalCommand)
    {
        return info.Reason switch
        {
            EmptyReason.NoOpMerge => $"{originalCommand} --allow-empty # No-op merge",
            EmptyReason.ChangesAlreadyApplied => $"# Skip: {originalCommand} # Changes already applied",
            _ => $"{originalCommand} --allow-empty # {info.Reason}"
        };
    }

    public static bool ShouldAutoSkip(this EmptyCommitInfo info)
    {
        return info.Reason switch
        {
            EmptyReason.ChangesAlreadyApplied => true,
            EmptyReason.NoOpMerge => true,
            EmptyReason.MergedChangesAlreadyPresent => true,
            _ => false
        };
    }
}

public static class EmptyCommitDetectionResultExtensions
{
    public static void DisplaySummary(this EmptyCommitDetectionResult result)
    {
        if (result.EmptyCommits.Count == 0)
        {
            AnsiConsole.MarkupLine("[green]No empty commits detected[/]");
            return;
        }

        var table = new Table()
            .Border(TableBorder.Rounded)
            .BorderColor(Color.Yellow)
            .AddColumn(new TableColumn("[bold]Reason[/]").LeftAligned())
            .AddColumn(new TableColumn("[bold]Count[/]").Centered())
            .AddColumn(new TableColumn("[bold]Example[/]").LeftAligned());

        foreach (var (reason, count) in result.ReasonCounts.OrderByDescending(r => r.Value))
        {
            var example = result.EmptyCommits.Values
                .FirstOrDefault(e => e.Reason == reason);

            table.AddRow(
                reason.ToString(),
                $"[yellow]{count}[/]",
                example != null ? $"[dim]{example.CommitSha.Substring(0, 8)}...[/]" : ""
            );
        }

        var panel = new Panel(table)
            .Header($"[yellow]🔍 Empty Commits Summary ({result.EmptyCommits.Count} total)[/]")
            .Border(BoxBorder.Rounded)
            .BorderColor(Color.Yellow);

        AnsiConsole.Write(panel);

        if (result.WarningMessages.Count > 0)
        {
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[dim]Warnings:[/]");
            foreach (var warning in result.WarningMessages.Take(5))
            {
                AnsiConsole.MarkupLine($"[dim]• {warning}[/]");
            }
        }

        AnsiConsole.MarkupLine($"\n[dim]Analysis completed in {result.DetectionTime.TotalSeconds:F2} seconds[/]");
    }
}