using CherryPickSmart.Models;
using LibGit2Sharp;
using Microsoft.Extensions.Caching.Memory;
using System.Security.Cryptography;
using System.Text;

namespace CherryPickSmart.Core.ConflictAnalysis;

/// <summary>
/// Conflict predictor with caching capabilities
/// </summary>
public class CachedConflictPredictor(
    IMemoryCache cache,
    ConflictPredictorOptions? options = null)
    : ConflictPredictor(options)
{
    private readonly TimeSpan _cacheExpiration = options?.CacheExpiration ?? TimeSpan.FromMinutes(5);

    /// <summary>
    /// Override to add caching
    /// </summary>
    protected override ConflictPrediction AnalyzeFileConflicts(
        Repository repo,
        string filePath,
        List<CpCommit> commits,
        Branch targetBranch,
        TargetBranchCache targetCache)
    {
        var cacheKey = GenerateCacheKey(filePath, commits, targetBranch.FriendlyName);

        // Try to get from cache
        if (cache.TryGetValue<ConflictPrediction>(cacheKey, out var cached))
        {
            return cached;
        }

        // Compute if not in cache
        var result = base.AnalyzeFileConflicts(repo, filePath, commits, targetBranch, targetCache);

        // Cache the result
        var cacheOptions = new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = _cacheExpiration,
            Size = 1 // Assuming each prediction counts as 1 unit
        };

        cache.Set(cacheKey, result, cacheOptions);

        return result;
    }

    /// <summary>
    /// Generate a unique cache key for the analysis
    /// </summary>
    private static string GenerateCacheKey(string file, List<CpCommit> commits, string targetBranch)
    {
        var keyBuilder = new StringBuilder();
        keyBuilder.Append($"conflict:{targetBranch}:{file}:");

        // Include commit SHAs in the key
        var commitHashes = string.Join(",", commits.Select(c => c.Sha.Substring(0, 8)).OrderBy(s => s));
        keyBuilder.Append(commitHashes);

        // Hash the key if it's too long
        if (keyBuilder.Length > 200)
        {
            using var sha256 = SHA256.Create();
            var hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(keyBuilder.ToString()));
            return $"conflict:{Convert.ToBase64String(hashBytes)}";
        }

        return keyBuilder.ToString();
    }

    /// <summary>
    /// Clear all cached predictions
    /// </summary>
    public void ClearCache()
    {
        if (cache is MemoryCache memCache)
        {
            memCache.Compact(1.0); // Remove all entries
        }
    }

    /// <summary>
    /// Get cache statistics
    /// </summary>
    public CacheStatistics GetCacheStatistics()
    {
        if (cache is MemoryCache memCache)
        {
            var stat = memCache.GetCurrentStatistics();
            
            return new CacheStatistics
            {
                CurrentEntries = stat?.CurrentEntryCount ?? memCache.Count,
                MemorySize = stat?.CurrentEstimatedSize ?? 0,
            };
        }

        return new CacheStatistics();
    }
}

/// <summary>
/// Cache statistics
/// </summary>
public class CacheStatistics
{
    public long CurrentEntries { get; set; }
    public long MemorySize { get; set; }
    public int CacheHits { get; set; }
    public int CacheMisses { get; set; }

    public double HitRate => CacheHits + CacheMisses > 0
        ? (double)CacheHits / (CacheHits + CacheMisses) * 100
        : 0;
}