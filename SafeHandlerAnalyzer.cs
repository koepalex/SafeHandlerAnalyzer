
using System.Threading.Tasks;
using System;
using Microsoft.Diagnostics.Runtime;
using SafeHandleAnalyzer.Configuration;
using SafeHandleAnalyzer;
using Microsoft.Extensions.Logging;
using Dumpify;
using System.Runtime.CompilerServices;
using System.Net;

// https://github.com/microsoft/clrmd/blob/main/src/Samples/GCRoot/GCRootDemo.cs


var loggerFactory = LoggingProvider.GetLoggerFactory(LogLevel.Debug);
var logger = loggerFactory.CreateLogger("SafeHandlerAnalyzer");

var settingsLoader = new SettingsLoader(loggerFactory.CreateLogger<SettingsLoader>());
var settings = await settingsLoader.LoadArguments(args).ConfigureAwait(false);

// Generate cache filename based on dump path or PID
string cacheFilePath;
if (settings.DumpPath is not null)
{
    // Use dump file name as base for cache file
    var dumpFileName = Path.GetFileNameWithoutExtension(settings.DumpPath);
    cacheFilePath = Path.Combine(Path.GetDirectoryName(settings.DumpPath) ?? ".", $"{dumpFileName}_analysis_cache.json");
}
else if (settings.ProcessId is not null)
{
    // Use PID as base for cache file
    cacheFilePath = $"pid_{settings.ProcessId}_analysis_cache.json";
}
else
{
    cacheFilePath = "analysis_cache.json";
}

// Initialize cache manager and load existing cache
var cacheManager = new AnalysisCacheManager(cacheFilePath, logger);
cacheManager.Load();

DataTarget? dataTarget = null;

if (settings.ProcessId is not null)
{
    logger.LogInformation($"Attaching to process ID: {settings.ProcessId}");
    dataTarget = DataTarget.AttachToProcess(settings.ProcessId.Value, suspend: false);
    logger.LogInformation("Successfully attached");
}
else if (settings.DumpPath is not null)
{
    logger.LogInformation($"Reading core dump from: {settings.DumpPath}");
    dataTarget = DataTarget.LoadDump(settings.DumpPath);
    logger.LogInformation("Successfully loaded");
}

if (dataTarget == null)
{
    logger.LogError("Can't load DataTarget - Stopping");
    return;
}
    
logger.LogInformation("Creating diagnostics runtime");
ClrRuntime? runtime;
try
{
    runtime = dataTarget.ClrVersions.Single().CreateRuntime();
    logger.LogInformation("Diagnostics runtime created");
}
catch
{
    logger.LogError("Couldn't create runtime, this typically happens when dump is analyzed on different OS than it was created on");
    return;
}

logger.LogInformation("Loading Finalizable objects");
IEnumerable<ClrObject> finalizableObjects = runtime.Heap.EnumerateFinalizableObjects();
logger.LogInformation("Finalizer queue loaded");

var safeHandleStatistics = new Dictionary<string, int>();
var gcRootAnalysisResults = new List<GCRootAnalysisResult>();

foreach(var finalizableObject in finalizableObjects)
{
    try
    {
        if (finalizableObject.Type?.Name?.StartsWith("Microsoft.Win32.SafeHandles") ?? false)
        {
            safeHandleStatistics.TryGetValue(finalizableObject.Type.Name, out int count);
            safeHandleStatistics[finalizableObject.Type.Name] = count + 1;

            if (finalizableObject.Type.Name == "Microsoft.Win32.SafeHandles.SafeFileHandle")
            {
                ClrInstanceField? pathField = finalizableObject.Type.GetFieldByName("_path");

                if (pathField != null)
                {
                    var path = pathField.ReadString(finalizableObject, false);
                    if (!string.IsNullOrEmpty(path))
                    {
                        logger.LogInformation($"Active File handle for {path}");
                    }
                }
                
            }
            else if (finalizableObject.Type?.Name?.StartsWith("Microsoft.Win32.SafeHandles.SafeMemoryMappedFileHandle") ?? false)
            {
            }
            else if (finalizableObject.Type?.Name?.StartsWith("Microsoft.Win32.SafeHandles.SafeX509StackHandle") ?? false)
            {
            }
            else if (finalizableObject.Type?.Name?.StartsWith("Microsoft.Win32.SafeHandles.SafeX509Handle") ?? false)
            {
            }
            else if (finalizableObject.Type?.Name?.StartsWith("Microsoft.Win32.SafeHandles.SafeWaitHandle") ?? false)
            {
            }
            else if (finalizableObject.Type?.Name?.StartsWith("Microsoft.Win32.SafeHandles.SafeX509StoreHandle") ?? false)
            {
            }
            else if (finalizableObject.Type?.Name?.StartsWith("Microsoft.Win32.SafeHandles.SafeHmacCtxHandle") ?? false)
            {
            }
            else if (finalizableObject.Type?.Name?.StartsWith("Microsoft.Win32.SafeHandles.SafeEvpCipherCtxHandle") ?? false)
            {
            }
            else if (finalizableObject.Type?.Name?.StartsWith("Microsoft.Win32.SafeHandles.SafeX509StoreCtxHandle") ?? false)
            {
            }
            else if (finalizableObject.Type?.Name?.StartsWith("Microsoft.Win32.SafeHandles.SafeMemoryMappedViewHandle") ?? false)
            {
            }
            else
            {
                finalizableObject.Type?.Name?.Dump();
            }

            if (settings.GcRootTypes != null && settings.GcRootTypes.Any(t => finalizableObject.Type?.Name?.EndsWith(t) ?? false))
            {
                // When GcRootTypes is specified, we always want to export results
                // So we analyze even if cached, to ensure exports are generated
                bool alreadyCached = cacheManager.IsAnalyzed(finalizableObject.Address);
                
                if (alreadyCached)
                {
                    var cachedInfo = cacheManager.GetCachedInfo(finalizableObject.Address);
                    logger.LogDebug($"Re-analyzing cached instance {finalizableObject.Type?.Name} @ {finalizableObject.Address:x} for export (cached: {cachedInfo?.RootPathCount} root(s))");
                }

                var analysisResult = AnalyzeGCRoots(runtime, finalizableObject, logger);
                if (analysisResult != null)
                {
                    // Export immediately after analysis
                    var exportedFiles = new List<string>();
                    
                    // Always export to individual text file
                    var fileExporter = new FileBasedGcRootExporter(logger);
                    var textFilePath = fileExporter.Export(analysisResult);
                    if (textFilePath != null)
                    {
                        exportedFiles.Add(textFilePath);
                    }
                    
                    gcRootAnalysisResults.Add(analysisResult);
                    
                    // Add to cache if not already there, including exported file paths
                    if (!alreadyCached)
                    {
                        cacheManager.AddAnalysis(
                            analysisResult.ObjectAddress, 
                            analysisResult.TypeName, 
                            analysisResult.RootPaths.Count,
                            exportedFiles.Count > 0 ? exportedFiles : null
                        );
                        cacheManager.Save();
                    }
                }
            }
        }
    }
    catch (Exception e)
    {
        logger.LogError(e.ToString());    
    }
}

logger.LogInformation("\n=== SafeHandle Statistics ===");
logger.LogInformation($"Total SafeHandle types found: {safeHandleStatistics.Count}");
foreach (var kvp in safeHandleStatistics.OrderByDescending(x => x.Value))
{
    logger.LogInformation($"  {kvp.Key}: {kvp.Value} instance(s)");
}
logger.LogInformation("============================\n");

// Generate SVG overlay graph if requested (individual files already exported during analysis)
if (gcRootAnalysisResults.Count > 0 && settings.GenerateGcRootImage)
{
    logger.LogInformation($"Generating SVG overlay graph for {gcRootAnalysisResults.Count} GC root analysis result(s)...");
    
    var svgExporter = new SvgGcRootExporter(logger);
    var svgFilePath = svgExporter.ExportOverlayedGraph(gcRootAnalysisResults);
    
    if (svgFilePath != null)
    {
        logger.LogInformation($"SVG overlay graph generated at: {svgFilePath}");
    }
}

// Final save of the cache with verbose logging
cacheManager.Save(verbose: true);

runtime?.Dispose();
dataTarget?.Dispose();

logger.LogInformation("SafeHandlerAnalyzer stopped");

static GCRootAnalysisResult? AnalyzeGCRoots(ClrRuntime runtime, ClrObject finalizableObject, ILogger logger)
{
    if (!finalizableObject.IsValid)
    {
        return null;
    }

    try
    {
        logger.LogDebug($"Analyzing GC roots for {finalizableObject.Type?.Name} @ {finalizableObject.Address:x}");
        
        string typeName = finalizableObject.Type?.Name ?? "Unknown";
        var rootPaths = new List<GCRootPath>();
        
        GCRoot gcroot = new GCRoot(runtime.Heap, new ulong[] { finalizableObject.Address });
        int pathCount = 0;
        
        foreach ((ClrRoot root, GCRoot.ChainLink path) in gcroot.EnumerateRootPaths())
        {
            pathCount++;
            var chain = new List<GCRootChainLink>();
            bool hasCircularDependency = false;
            bool maxDepthReached = false;
            
            var current = path;
            int depth = 0;
            const int maxDepth = 1000; // Safety limit to prevent infinite loops
            var visitedAddresses = new HashSet<ulong>();
            
            while (current != null)
            {
                // Check for circular dependency
                if (!visitedAddresses.Add(current.Object))
                {
                    logger.LogWarning($"Circular dependency detected at 0x{current.Object:x}");
                    hasCircularDependency = true;
                    break;
                }
                
                // Check for maximum depth
                if (depth >= maxDepth)
                {
                    logger.LogWarning($"Maximum depth reached, stopping traversal");
                    maxDepthReached = true;
                    break;
                }
                
                ClrObject obj = runtime.Heap.GetObject(current.Object);
                chain.Add(new GCRootChainLink(
                    Address: current.Object,
                    TypeName: obj.Type?.Name ?? "Unknown",
                    Depth: depth
                ));
                
                current = current.Next;
                depth++;
            }
            
            rootPaths.Add(new GCRootPath(
                RootKind: root.RootKind,
                RootAddress: root.Address,
                PathNumber: pathCount,
                Chain: chain,
                HasCircularDependency: hasCircularDependency,
                MaxDepthReached: maxDepthReached
            ));
        }
        
        return new GCRootAnalysisResult(
            TypeName: typeName,
            ObjectAddress: finalizableObject.Address,
            AnalysisDate: DateTime.Now,
            RootPaths: rootPaths
        );
    }
    catch (Exception ex)
    {
        logger.LogError(ex, $"Error analyzing GC roots for {finalizableObject.Address:x}");
        return null;
    }
}
