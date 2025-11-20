
using System.Threading.Tasks;
using System;
using Microsoft.Diagnostics.Runtime;
using SafeHandleAnalyzer.Configuration;
using Microsoft.Extensions.Logging;
using Dumpify;
using System.Runtime.CompilerServices;
using System.Net;

// https://github.com/microsoft/clrmd/blob/main/src/Samples/GCRoot/GCRootDemo.cs


var loggerFactory = LoggingProvider.GetLoggerFactory(LogLevel.Debug);
var logger = loggerFactory.CreateLogger("SafeHandlerAnalyzer");

var settingsLoader = new SettingsLoader(loggerFactory.CreateLogger<SettingsLoader>());
var settings = await settingsLoader.LoadArguments(args).ConfigureAwait(false);

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
            else
            {
                finalizableObject.Type?.Name?.Dump();
            }

            if (settings.GcRootTypes != null && settings.GcRootTypes.Any(t => finalizableObject.Type?.Name?.EndsWith(t) ?? false))
            {
                AnalyzeGCRoots(runtime, finalizableObject, logger);
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

runtime?.Dispose();
dataTarget?.Dispose();

logger.LogInformation("SafeHandlerAnalyzer stopped");

static void AnalyzeGCRoots(ClrRuntime runtime, ClrObject finalizableObject, ILogger logger)
{
    if (!finalizableObject.IsValid)
    {
        return;
    }

    try
    {
        logger.LogDebug($"Analyzing GC roots for {finalizableObject.Type?.Name} @ {finalizableObject.Address:x}");
        
        // Create folder structure based on type name
        string typeName = finalizableObject.Type?.Name ?? "Unknown";
        string sanitizedTypeName = string.Join("_", typeName.Split(Path.GetInvalidFileNameChars()));
        string outputFolder = Path.Combine(Environment.CurrentDirectory, "GCRoots", sanitizedTypeName);
        Directory.CreateDirectory(outputFolder);
        
        // Create file for this object using its address as filename
        string fileName = $"{finalizableObject.Address:x16}.txt";
        string filePath = Path.Combine(outputFolder, fileName);
        
        using (StreamWriter writer = new StreamWriter(filePath))
        {
            writer.WriteLine($"GC Root Analysis for {typeName}");
            writer.WriteLine($"Object Address: 0x{finalizableObject.Address:x}");
            writer.WriteLine($"Analysis Date: {DateTime.Now}");
            writer.WriteLine(new string('=', 80));
            writer.WriteLine();
            
            GCRoot gcroot = new GCRoot(runtime.Heap, new ulong[] { finalizableObject.Address });
            int pathCount = 0;
            
            foreach ((ClrRoot root, GCRoot.ChainLink path) in gcroot.EnumerateRootPaths())
            {
                pathCount++;
                string rootInfo = $"GC Root path #{pathCount}: {root.RootKind} @ 0x{root.Address:x}";
                writer.WriteLine(rootInfo);
                
                var current = path;
                int depth = 0;
                const int maxDepth = 1000; // Safety limit to prevent infinite loops
                var visitedAddresses = new HashSet<ulong>();
                
                while (current != null)
                {
                    // Check for circular dependency
                    if (!visitedAddresses.Add(current.Object))
                    {
                        string circularMsg = $"    [{depth}] Circular dependency detected at 0x{current.Object:x}";
                        logger.LogWarning(circularMsg);
                        writer.WriteLine(circularMsg);
                        break;
                    }
                    
                    // Check for maximum depth
                    if (depth >= maxDepth)
                    {
                        string maxDepthMsg = $"    [{depth}] Maximum depth reached, stopping traversal";
                        logger.LogWarning(maxDepthMsg);
                        writer.WriteLine(maxDepthMsg);
                        break;
                    }
                    
                    ClrObject obj = runtime.Heap.GetObject(current.Object);
                    string objInfo = $"    --> [{depth}] {obj.Type?.Name ?? "Unknown"} @ 0x{current.Object:x}";
                    writer.WriteLine(objInfo);
                    current = current.Next;
                    depth++;
                }
                
                writer.WriteLine();
            }
            
            if (pathCount == 0)
            {
                string orphanMsg = $"No GC root paths found for 0x{finalizableObject.Address:x} (orphaned object?)";
                logger.LogWarning($"  {orphanMsg}");
                writer.WriteLine(orphanMsg);
            }
            else
            {
                writer.WriteLine($"Total GC root paths found: {pathCount}");
            }
        }
        
        logger.LogInformation($"  GC root analysis written to: {filePath}");
    }
    catch (Exception ex)
    {
        logger.LogError(ex, $"Error analyzing GC roots for {finalizableObject.Address:x}");
    }
}