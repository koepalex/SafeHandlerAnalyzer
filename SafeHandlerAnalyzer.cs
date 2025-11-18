#:package Microsoft.Diagnostics.Runtime@3.1.512801
#:package System.CommandLine@2.0.0


using System.Threading.Tasks;
using System.CommandLine;
using System.CommandLine.Invocation;
using System;
using Microsoft.Diagnostics.Runtime;

// https://github.com/microsoft/clrmd/blob/main/src/Samples/GCRoot/GCRootDemo.cs
var settings = await LoadArguments(args);
DataTarget? dataTarget = null;

if (settings.ProcessId is not null)
{
    Console.WriteLine($"Attaching to process ID: {settings.ProcessId}");
    dataTarget = DataTarget.AttachToProcess(settings.ProcessId.Value, suspend: false);
}
else if (settings.DumpPath is not null)
{
    Console.WriteLine($"Reading core dump from: {settings.DumpPath}");
    dataTarget = DataTarget.LoadDump(settings.DumpPath);
}
    
using ClrRuntime runtime = dataTarget.ClrVersions.Single().CreateRuntime();
foreach(var finalizableObjects in runtime.Heap.EnumerateFinalizableObjects())
{
    Console.WriteLine($"Object of type {finalizableObjects.Type.Name} is waiting finalization.");
    finalizableObjects.ReadObjectField("");
}

dataTarget?.Dispose();

async Task<Settings> LoadArguments(string[] args) {
    
    var pidOption = new Option<int?>(
        name: "-p",
        aliases: new[] { "--pid" })
        {
            Description = "Process ID to attach to",
            Required = false,
        };

    var dumpOption = new Option<string?>(
        name: "-d",
        aliases: new[] { "--dump" })
        {
            Description = "Path to core dump",
            Required = false,
        };

    var rootCommand = new RootCommand("Attach to process or read core dump");
    rootCommand.Add(pidOption);
    rootCommand.Add(dumpOption);
   
    var parseResult = rootCommand.Parse(args);
    var settings = new Settings
    {
        ProcessId = parseResult.GetValue<int?>(pidOption),
        DumpPath = parseResult.GetValue<string?>(dumpOption),
    };

    if (settings.ProcessId is null && settings.DumpPath is null)
    {
        Console.Error.WriteLine("Either --pid or --dump must be specified.");
        Environment.Exit(-1);
    }

    return settings;
}

public record Settings
{
    public int? ProcessId { get; set; }
    public string? DumpPath { get; set; }
}