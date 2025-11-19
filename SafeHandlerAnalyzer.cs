
using System.Threading.Tasks;
using System;
using Microsoft.Diagnostics.Runtime;
using SafeHandleAnalyzer.Configuration;
using Microsoft.Extensions.Logging;
using Dumpify;
using System.Runtime.CompilerServices;

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

foreach(var finalizableObject in finalizableObjects)
{
    if (finalizableObject.Type?.Name?.StartsWith("Microsoft.Win32.SafeHandles") ?? false)
    {
        if (finalizableObject.Type.Name == "Microsoft.Win32.SafeHandles.SafeFileHandle")
        {
            ClrInstanceField? pathField = finalizableObject.Type.GetFieldByName("_path");

            if (pathField != null)
            {
                var path = pathField.ReadString(finalizableObject, false);
                logger.LogInformation($"Active File handle for {path}");
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
        else if (finalizableObject.Type?.Name?.StartsWith("Microsoft.Win32.SafeHandles.SafeX509StoreHandle") ?? false)
        {
            
        }
        else if (finalizableObject.Type?.Name?.StartsWith("Microsoft.Win32.SafeHandles.SafeHmacCtxHandle") ?? false)
        {
            
        }
        else if (finalizableObject.Type?.Name?.StartsWith("Microsoft.Win32.SafeHandles.SafeEvpCipherCtxHandle") ?? false)
        {
            
        }
        else
        {
            finalizableObject.Type.Name.Dump();
        }
    }
}

runtime?.Dispose();
dataTarget?.Dispose();

logger.LogInformation("SafeHandlerAnalyzer stopped");