using Microsoft.Extensions.Logging;
// Exporter for writing GC Root analysis to files
class FileBasedGcRootExporter
{
    private readonly ILogger _logger;

    public FileBasedGcRootExporter(ILogger logger)
    {
        _logger = logger;
    }

    public void Export(GCRootAnalysisResult result)
    {
        try
        {
            // Create folder structure based on type name
            string sanitizedTypeName = string.Join("_", result.TypeName.Split(Path.GetInvalidFileNameChars()));
            string outputFolder = Path.Combine(Environment.CurrentDirectory, "GCRoots", sanitizedTypeName);
            Directory.CreateDirectory(outputFolder);
            
            // Create file for this object using its address as filename
            string fileName = $"{result.ObjectAddress:x16}.txt";
            string filePath = Path.Combine(outputFolder, fileName);
            
            using (StreamWriter writer = new StreamWriter(filePath))
            {
                writer.WriteLine($"GC Root Analysis for {result.TypeName}");
                writer.WriteLine($"Object Address: 0x{result.ObjectAddress:x}");
                writer.WriteLine($"Analysis Date: {result.AnalysisDate}");
                writer.WriteLine(new string('=', 80));
                writer.WriteLine();
                
                if (result.RootPaths.Count == 0)
                {
                    string orphanMsg = $"No GC root paths found for 0x{result.ObjectAddress:x} (orphaned object?)";
                    _logger.LogWarning($"  {orphanMsg}");
                    writer.WriteLine(orphanMsg);
                }
                else
                {
                    foreach (var rootPath in result.RootPaths)
                    {
                        string rootInfo = $"GC Root path #{rootPath.PathNumber}: {rootPath.RootKind} @ 0x{rootPath.RootAddress:x}";
                        writer.WriteLine(rootInfo);
                        
                        foreach (var link in rootPath.Chain)
                        {
                            string objInfo = $"    --> [{link.Depth}] {link.TypeName} @ 0x{link.Address:x}";
                            writer.WriteLine(objInfo);
                        }
                        
                        if (rootPath.HasCircularDependency)
                        {
                            writer.WriteLine($"    Circular dependency detected");
                        }
                        
                        if (rootPath.MaxDepthReached)
                        {
                            writer.WriteLine($"    Maximum depth reached, stopping traversal");
                        }
                        
                        writer.WriteLine();
                    }
                    
                    writer.WriteLine($"Total GC root paths found: {result.RootPaths.Count}");
                }
            }
            
            _logger.LogInformation($"  GC root analysis written to: {filePath}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error exporting GC root analysis for {result.ObjectAddress:x}");
        }
    }

    public void ExportAll(IEnumerable<GCRootAnalysisResult> results)
    {
        foreach (var result in results)
        {
            Export(result);
        }
    }
}