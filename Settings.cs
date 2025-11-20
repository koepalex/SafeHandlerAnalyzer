
using System.Collections.Immutable;
using System.CommandLine;
using Microsoft.Extensions.Logging;

namespace SafeHandleAnalyzer.Configuration;

public class SettingsLoader
{
    private ILogger<SettingsLoader> _logger;

    internal SettingsLoader(ILogger<SettingsLoader> logger)
    {
        _logger = logger;
    }

    internal async Task<Settings> LoadArguments(string[] args) {
        
        var pidOption = new Option<int?>(
            name: "-p",
            aliases: ["--pid"])
            {
                Description = "Process ID to attach to",
                Required = false,
            };

        var dumpOption = new Option<string?>(
            name: "-d",
            aliases: ["--dump"])
            {
                Description = "Path to core dump",
                Required = false,
            };

        var gcRootOption = new Option<string?>(
            name: "-gct",
            aliases: ["--gcroottypes"])
            {
                Description = "Comma separated string of SafeHandle types, that should generate gc root files",
                Required = false,
            };

        var rootCommand = new RootCommand("Attach to process or read core dump")
        {
            pidOption,
            dumpOption,
            gcRootOption,
        };
    
        var parseResult = rootCommand.Parse(args);
        var settings = new Settings
        {
            ProcessId = parseResult.GetValue<int?>(pidOption),
            DumpPath = parseResult.GetValue<string?>(dumpOption),
            GcRootTypes = parseResult.GetValue<string?>(gcRootOption)?.Split(',')?.ToImmutableList(),
        };

        if (settings.ProcessId is null && settings.DumpPath is null)
        {
            _logger.LogError("Either --pid or --dump must be specified.");
            Environment.Exit(-1);
        }

        return settings;
    }
}

internal record Settings
{
    internal int? ProcessId { get; set; }
    internal string? DumpPath { get; set; }
    internal IEnumerable<string>? GcRootTypes { get; set; }
}