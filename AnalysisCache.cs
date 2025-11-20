
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace SafeHandleAnalyzer;

/// <summary>
/// Represents a cached analysis result for a single object instance
/// </summary>
/// <param name="ObjectAddress">The memory address of the analyzed object</param>
/// <param name="TypeName">The full type name of the object</param>
/// <param name="RootPathCount">Number of GC root paths found for this object</param>
/// <param name="AnalysisDate">When the analysis was performed</param>
record AnalysisCache
(
    ulong ObjectAddress,
    string TypeName,
    int RootPathCount,
    DateTime AnalysisDate
);

/// <summary>
/// Container for all cached analysis results
/// </summary>
record AnalysisCacheData
(
    Dictionary<ulong, AnalysisCache> AnalyzedInstances
);

/// <summary>
/// Manages loading and saving of analysis cache to disk
/// </summary>
class AnalysisCacheManager
{
    private readonly ILogger _logger;
    private readonly string _cacheFilePath;
    private Dictionary<ulong, AnalysisCache> _cache;

    public AnalysisCacheManager(string cacheFilePath, ILogger logger)
    {
        _cacheFilePath = cacheFilePath;
        _logger = logger;
        _cache = new Dictionary<ulong, AnalysisCache>();
    }

    /// <summary>
    /// Loads the cache from disk if it exists
    /// </summary>
    public void Load()
    {
        if (!File.Exists(_cacheFilePath))
        {
            _logger.LogInformation($"No cache file found at {_cacheFilePath}, starting fresh");
            return;
        }

        try
        {
            var json = File.ReadAllText(_cacheFilePath);
            var cacheData = JsonSerializer.Deserialize<AnalysisCacheData>(json);
            
            if (cacheData?.AnalyzedInstances != null)
            {
                _cache = cacheData.AnalyzedInstances;
                _logger.LogInformation($"Loaded {_cache.Count} cached analysis result(s) from {_cacheFilePath}");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error loading cache from {_cacheFilePath}, starting fresh");
            _cache = new Dictionary<ulong, AnalysisCache>();
        }
    }

    /// <summary>
    /// Checks if an instance was already analyzed
    /// </summary>
    public bool IsAnalyzed(ulong objectAddress)
    {
        return _cache.ContainsKey(objectAddress);
    }

    /// <summary>
    /// Gets cached information for an instance if available
    /// </summary>
    public AnalysisCache? GetCachedInfo(ulong objectAddress)
    {
        return _cache.TryGetValue(objectAddress, out var info) ? info : null;
    }

    /// <summary>
    /// Adds a new analysis result to the cache
    /// </summary>
    public void AddAnalysis(ulong objectAddress, string typeName, int rootPathCount)
    {
        _cache[objectAddress] = new AnalysisCache(
            ObjectAddress: objectAddress,
            TypeName: typeName,
            RootPathCount: rootPathCount,
            AnalysisDate: DateTime.Now
        );
    }

    /// <summary>
    /// Saves the cache to disk with atomic write (writes to temp file first)
    /// </summary>
    /// <param name="verbose">Whether to log informational messages (default: false for frequent saves)</param>
    public void Save(bool verbose = false)
    {
        try
        {
            var cacheData = new AnalysisCacheData(_cache);
            var options = new JsonSerializerOptions 
            { 
                WriteIndented = true 
            };
            var json = JsonSerializer.Serialize(cacheData, options);
            
            // Atomic write: write to temp file first, then replace
            var tempFilePath = _cacheFilePath + ".tmp";
            File.WriteAllText(tempFilePath, json);
            File.Move(tempFilePath, _cacheFilePath, overwrite: true);
            
            if (verbose)
            {
                _logger.LogInformation($"Saved {_cache.Count} analysis result(s) to cache at {_cacheFilePath}");
            }
            else
            {
                _logger.LogDebug($"Cache updated: {_cache.Count} entries");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error saving cache to {_cacheFilePath}");
        }
    }

    /// <summary>
    /// Gets the count of currently cached instances
    /// </summary>
    public int CachedCount => _cache.Count;
}
