using Microsoft.Extensions.Logging;
using System.Text;
using System.Xml;

// Exporter for generating SVG visualization of overlayed GC Root graphs
class SvgGcRootExporter
{
    private readonly ILogger _logger;
    private readonly int _nodeWidth = 180;
    private readonly int _nodeHeight = 40;
    private readonly int _horizontalSpacing = 50;
    private readonly int _verticalSpacing = 60;
    private readonly int _maxDepthToVisualize = 20;

    public SvgGcRootExporter(ILogger logger)
    {
        _logger = logger;
    }

    public string? ExportOverlayedGraph(IEnumerable<GCRootAnalysisResult> results, string outputPath = "gc_roots_overlay.svg")
    {
        try
        {
            _logger.LogInformation($"Generating overlayed GC root graph SVG...");

            // Build a unified graph structure
            var graphNodes = new Dictionary<ulong, GraphNode>();
            var graphEdges = new List<GraphEdge>();

            foreach (var result in results)
            {
                foreach (var rootPath in result.RootPaths)
                {
                    ProcessRootPath(rootPath, result.TypeName, graphNodes, graphEdges);
                }
            }

            if (graphNodes.Count == 0)
            {
                _logger.LogWarning("No nodes to visualize");
                return null;
            }

            // Layout the graph
            var layout = CalculateLayout(graphNodes, graphEdges);

            // Generate SVG
            GenerateSvg(layout, graphNodes, graphEdges, outputPath);

            _logger.LogInformation($"GC root overlay graph saved to: {outputPath}");
            return outputPath;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating overlayed GC root graph");
            return null;
        }
    }

    private void ProcessRootPath(GCRootPath rootPath, string targetTypeName, Dictionary<ulong, GraphNode> nodes, List<GraphEdge> edges)
    {
        // Add root node
        if (!nodes.ContainsKey(rootPath.RootAddress))
        {
            nodes[rootPath.RootAddress] = new GraphNode(
                Address: rootPath.RootAddress,
                TypeName: $"[{rootPath.RootKind}]",
                IsRoot: true,
                Depth: 0,
                ReferenceCount: 0
            );
        }
        nodes[rootPath.RootAddress] = nodes[rootPath.RootAddress] with { ReferenceCount = nodes[rootPath.RootAddress].ReferenceCount + 1 };

        ulong previousAddress = rootPath.RootAddress;

        // Process chain links
        int depth = 1;
        foreach (var link in rootPath.Chain)
        {
            if (depth > _maxDepthToVisualize)
                break;

            if (!nodes.ContainsKey(link.Address))
            {
                nodes[link.Address] = new GraphNode(
                    Address: link.Address,
                    TypeName: link.TypeName,
                    IsRoot: false,
                    Depth: depth,
                    ReferenceCount: 0
                );
            }
            else
            {
                // Update depth to minimum (closer to root)
                if (depth < nodes[link.Address].Depth)
                {
                    nodes[link.Address] = nodes[link.Address] with { Depth = depth };
                }
            }
            nodes[link.Address] = nodes[link.Address] with { ReferenceCount = nodes[link.Address].ReferenceCount + 1 };

            // Add edge if not already present
            var edge = new GraphEdge(previousAddress, link.Address);
            if (!edges.Any(e => e.From == edge.From && e.To == edge.To))
            {
                edges.Add(edge);
            }

            previousAddress = link.Address;
            depth++;
        }
    }

    private Dictionary<ulong, NodePosition> CalculateLayout(Dictionary<ulong, GraphNode> nodes, List<GraphEdge> edges)
    {
        var layout = new Dictionary<ulong, NodePosition>();
        
        // Group nodes by depth
        var nodesByDepth = nodes.Values
            .GroupBy(n => n.Depth)
            .OrderBy(g => g.Key)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(n => n.ReferenceCount).ToList());

        int currentY = 50;

        foreach (var depthGroup in nodesByDepth.OrderBy(kvp => kvp.Key))
        {
            int depth = depthGroup.Key;
            var depthNodes = depthGroup.Value;
            
            // Calculate total width needed for this level
            int totalWidth = depthNodes.Count * (_nodeWidth + _horizontalSpacing);
            int startX = Math.Max(50, (2000 - totalWidth) / 2); // Center or start at 50

            int currentX = startX;
            foreach (var node in depthNodes)
            {
                layout[node.Address] = new NodePosition(currentX, currentY);
                currentX += _nodeWidth + _horizontalSpacing;
            }

            currentY += _nodeHeight + _verticalSpacing;
        }

        return layout;
    }

    private void GenerateSvg(Dictionary<ulong, NodePosition> layout, Dictionary<ulong, GraphNode> nodes, List<GraphEdge> edges, string outputPath)
    {
        // Calculate SVG dimensions
        int maxX = layout.Values.Max(p => p.X) + _nodeWidth + 100;
        int maxY = layout.Values.Max(p => p.Y) + _nodeHeight + 100;

        var svg = new StringBuilder();
        svg.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        svg.AppendLine($"<svg width=\"{maxX}\" height=\"{maxY}\" xmlns=\"http://www.w3.org/2000/svg\">");
        
        // Add styles
        svg.AppendLine("<defs>");
        svg.AppendLine("  <style>");
        svg.AppendLine("    .node { fill: #e3f2fd; stroke: #1976d2; stroke-width: 2; }");
        svg.AppendLine("    .root-node { fill: #ffebee; stroke: #c62828; stroke-width: 2; }");
        svg.AppendLine("    .node-text { font-family: monospace; font-size: 12px; fill: #000; }");
        svg.AppendLine("    .node-address { font-family: monospace; font-size: 10px; fill: #666; }");
        svg.AppendLine("    .edge { stroke: #90caf9; stroke-width: 1.5; fill: none; opacity: 0.6; }");
        svg.AppendLine("    .edge-hover { stroke: #1976d2; stroke-width: 2.5; }");
        svg.AppendLine("    .count-badge { fill: #ff9800; stroke: #e65100; stroke-width: 1; }");
        svg.AppendLine("    .count-text { font-family: sans-serif; font-size: 11px; fill: #fff; font-weight: bold; }");
        svg.AppendLine("  </style>");
        svg.AppendLine("</defs>");

        // Add background
        svg.AppendLine($"<rect width=\"{maxX}\" height=\"{maxY}\" fill=\"#fafafa\"/>");

        // Group edges by count (how many times they appear)
        var edgeCounts = edges.GroupBy(e => $"{e.From}-{e.To}")
            .ToDictionary(g => g.Key, g => g.Count());

        // Draw edges first (so they appear behind nodes)
        svg.AppendLine("<g id=\"edges\">");
        var drawnEdges = new HashSet<string>();
        foreach (var edge in edges)
        {
            string edgeKey = $"{edge.From}-{edge.To}";
            if (drawnEdges.Contains(edgeKey))
                continue;
            drawnEdges.Add(edgeKey);

            if (layout.TryGetValue(edge.From, out var fromPos) && layout.TryGetValue(edge.To, out var toPos))
            {
                int x1 = fromPos.X + _nodeWidth / 2;
                int y1 = fromPos.Y + _nodeHeight;
                int x2 = toPos.X + _nodeWidth / 2;
                int y2 = toPos.Y;

                // Use bezier curve for smoother edges
                int controlY = (y1 + y2) / 2;
                svg.AppendLine($"  <path d=\"M {x1},{y1} C {x1},{controlY} {x2},{controlY} {x2},{y2}\" class=\"edge\" marker-end=\"url(#arrowhead)\"/>");
            }
        }
        svg.AppendLine("</g>");

        // Add arrow marker definition
        svg.AppendLine("<defs>");
        svg.AppendLine("  <marker id=\"arrowhead\" markerWidth=\"10\" markerHeight=\"10\" refX=\"9\" refY=\"3\" orient=\"auto\">");
        svg.AppendLine("    <polygon points=\"0 0, 10 3, 0 6\" fill=\"#90caf9\" />");
        svg.AppendLine("  </marker>");
        svg.AppendLine("</defs>");

        // Draw nodes
        svg.AppendLine("<g id=\"nodes\">");
        foreach (var node in nodes.Values.OrderBy(n => n.Depth))
        {
            if (layout.TryGetValue(node.Address, out var pos))
            {
                string nodeClass = node.IsRoot ? "root-node" : "node";
                
                // Draw node rectangle
                svg.AppendLine($"  <rect x=\"{pos.X}\" y=\"{pos.Y}\" width=\"{_nodeWidth}\" height=\"{_nodeHeight}\" class=\"{nodeClass}\" rx=\"5\"/>");
                
                // Draw type name (truncate if too long)
                string displayName = TruncateText(node.TypeName, 22);
                svg.AppendLine($"  <text x=\"{pos.X + _nodeWidth / 2}\" y=\"{pos.Y + 18}\" class=\"node-text\" text-anchor=\"middle\">{EscapeXml(displayName)}</text>");
                
                // Draw address
                svg.AppendLine($"  <text x=\"{pos.X + _nodeWidth / 2}\" y=\"{pos.Y + 32}\" class=\"node-address\" text-anchor=\"middle\">0x{node.Address:x}</text>");
                
                // Draw reference count badge if > 1
                if (node.ReferenceCount > 1)
                {
                    int badgeX = pos.X + _nodeWidth - 15;
                    int badgeY = pos.Y - 8;
                    svg.AppendLine($"  <circle cx=\"{badgeX}\" cy=\"{badgeY}\" r=\"12\" class=\"count-badge\"/>");
                    svg.AppendLine($"  <text x=\"{badgeX}\" y=\"{badgeY + 4}\" class=\"count-text\" text-anchor=\"middle\">{node.ReferenceCount}</text>");
                }
            }
        }
        svg.AppendLine("</g>");

        // Add legend
        svg.AppendLine("<g id=\"legend\">");
        int legendX = 20;
        int legendY = maxY - 100;
        svg.AppendLine($"  <text x=\"{legendX}\" y=\"{legendY}\" class=\"node-text\" font-weight=\"bold\">Legend:</text>");
        svg.AppendLine($"  <rect x=\"{legendX}\" y=\"{legendY + 10}\" width=\"30\" height=\"20\" class=\"root-node\" rx=\"3\"/>");
        svg.AppendLine($"  <text x=\"{legendX + 40}\" y=\"{legendY + 24}\" class=\"node-text\">GC Root</text>");
        svg.AppendLine($"  <rect x=\"{legendX}\" y=\"{legendY + 35}\" width=\"30\" height=\"20\" class=\"node\" rx=\"3\"/>");
        svg.AppendLine($"  <text x=\"{legendX + 40}\" y=\"{legendY + 49}\" class=\"node-text\">Object</text>");
        svg.AppendLine($"  <circle cx=\"{legendX + 15}\" cy=\"{legendY + 70}\" r=\"12\" class=\"count-badge\"/>");
        svg.AppendLine($"  <text x=\"{legendX + 15}\" y=\"{legendY + 74}\" class=\"count-text\" text-anchor=\"middle\">N</text>");
        svg.AppendLine($"  <text x=\"{legendX + 40}\" y=\"{legendY + 74}\" class=\"node-text\">Reference count</text>");
        svg.AppendLine("</g>");

        // Add title
        svg.AppendLine($"<text x=\"{maxX / 2}\" y=\"30\" class=\"node-text\" text-anchor=\"middle\" font-size=\"18\" font-weight=\"bold\">GC Root Overlay Graph ({nodes.Count} nodes, {edges.Count} edges)</text>");

        svg.AppendLine("</svg>");

        File.WriteAllText(outputPath, svg.ToString());
    }

    private string TruncateText(string text, int maxLength)
    {
        if (text.Length <= maxLength)
            return text;

        // Try to truncate at last dot
        int lastDot = text.LastIndexOf('.', maxLength);
        if (lastDot > maxLength / 2)
            return "..." + text.Substring(lastDot);

        return text.Substring(0, maxLength - 3) + "...";
    }

    private string EscapeXml(string text)
    {
        return text.Replace("&", "&amp;")
                   .Replace("<", "&lt;")
                   .Replace(">", "&gt;")
                   .Replace("\"", "&quot;")
                   .Replace("'", "&apos;");
    }
}

// Helper classes for graph representation
record GraphNode(
    ulong Address,
    string TypeName,
    bool IsRoot,
    int Depth,
    int ReferenceCount
);

record GraphEdge(ulong From, ulong To);

record NodePosition(int X, int Y);
