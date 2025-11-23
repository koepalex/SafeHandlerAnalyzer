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

    public string? ExportOverlayedGraph(GCRootAnalysisResult result)
    {
        try
        {
            string sanitizedTypeName = string.Join("_", result.TypeName.Split(Path.GetInvalidFileNameChars()));
            string outputFolder = Path.Combine(Environment.CurrentDirectory, "GCRoots", sanitizedTypeName);
            Directory.CreateDirectory(outputFolder);
            
            // Create file for this object using its address as filename
            string fileName = $"{result.ObjectAddress:x16}.svg";
            string filePath = Path.Combine(outputFolder, fileName);
            _logger.LogInformation($"Generating overlayed GC root graph SVG...");

            // Build a unified graph structure
            var graphNodes = new Dictionary<ulong, GraphNode>();
            var graphEdges = new List<GraphEdge>();

            foreach (var rootPath in result.RootPaths)
            {
                ProcessRootPath(rootPath, result.TypeName, graphNodes, graphEdges);
            }

            if (graphNodes.Count == 0)
            {
                _logger.LogWarning("No nodes to visualize");
                return null;
            }

            // Depth 0 will represent the leaf (finalizable) object; increasing depth moves towards GC roots
            var layout = CalculateLayout(graphNodes, graphEdges, result.ObjectAddress);

            // Generate SVG
            GenerateSvg(layout, graphNodes, graphEdges, filePath, result.ObjectAddress);

            return filePath;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating overlayed GC root graph");
            return null;
        }
    }

    private void ProcessRootPath(GCRootPath rootPath, string targetTypeName, Dictionary<ulong, GraphNode> nodes, List<GraphEdge> edges)
    {
        // Assemble full path sequence root -> ... -> leaf
        var sequence = new List<(ulong Address, string TypeName, bool IsRoot)>
        {
            (rootPath.RootAddress, $"[{rootPath.RootKind}]", true)
        };
        foreach (var link in rootPath.Chain)
        {
            sequence.Add((link.Address, link.TypeName, false));
        }

        if (sequence.Count == 0)
        {
            return;
        }

        // Leaf assumed to be last element (finalizable object)
        var leaf = sequence[^1];
        ulong leafAddress = leaf.Address;

        if (!nodes.ContainsKey(leafAddress))
        {
            nodes[leafAddress] = new GraphNode(
                Address: leafAddress,
                TypeName: targetTypeName,
                IsRoot: false,
                Depth: 0,
                ReferenceCount: 0
            );
        }
        else if (nodes[leafAddress].Depth > 0)
        {
            nodes[leafAddress] = nodes[leafAddress] with { Depth = 0 }; // ensure leaf stays depth 0
        }
        nodes[leafAddress] = nodes[leafAddress] with { ReferenceCount = nodes[leafAddress].ReferenceCount + 1 };

        ulong previousAddress = leafAddress; // previous in traversal towards roots (child reference)
        int depth = 1; // parents of leaf start at depth 1

        // Walk backwards towards root
        for (int i = sequence.Count - 2; i >= 0; i--)
        {
            if (depth > _maxDepthToVisualize)
            {
                break;
            }

            var current = sequence[i];
            if (!nodes.ContainsKey(current.Address))
            {
                nodes[current.Address] = new GraphNode(
                    Address: current.Address,
                    TypeName: current.TypeName,
                    IsRoot: current.IsRoot,
                    Depth: depth,
                    ReferenceCount: 0
                );
            }
            else if (depth < nodes[current.Address].Depth)
            {
                nodes[current.Address] = nodes[current.Address] with { Depth = depth };
            }
            nodes[current.Address] = nodes[current.Address] with { ReferenceCount = nodes[current.Address].ReferenceCount + 1 };

            // Edge from parent (current) to child (previousAddress)
            var edge = new GraphEdge(current.Address, previousAddress);
            if (!edges.Any(e => e.From == edge.From && e.To == edge.To))
            {
                edges.Add(edge);
            }
            previousAddress = current.Address;
            depth++;
        }
    }

    private Dictionary<ulong, NodePosition> CalculateLayout(Dictionary<ulong, GraphNode> nodes, List<GraphEdge> edges, ulong analyzedObjectAddress)
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

    private void GenerateSvg(Dictionary<ulong, NodePosition> layout, Dictionary<ulong, GraphNode> nodes, List<GraphEdge> edges, string outputPath, ulong analyzedObjectAddress)
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
        svg.AppendLine("    .target-node { fill: #fff3e0; stroke: #ef6c00; stroke-width: 3; }");
        svg.AppendLine("    .node-text { font-family: monospace; font-size: 12px; fill: #000; }");
        svg.AppendLine("    .node-address { font-family: monospace; font-size: 10px; fill: #666; }");
        svg.AppendLine("    .edge { stroke: #90caf9; stroke-width: 1.5; fill: none; opacity: 0.6; }");
        svg.AppendLine("    .edge-hover { stroke: #1976d2; stroke-width: 2.5; }");
        svg.AppendLine("    .count-badge { fill: #ff9800; stroke: #e65100; stroke-width: 1; }");
        svg.AppendLine("    .count-text { font-family: sans-serif; font-size: 11px; fill: #fff; font-weight: bold; }");
        svg.AppendLine("    .copy-flash { animation: flash 1s ease-in-out; }");
        svg.AppendLine("    @keyframes flash { 0% { stroke-width:3; } 50% { stroke-width:6; } 100% { stroke-width:3; } }");
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
                if (node.Address == analyzedObjectAddress)
                {
                    nodeClass = "target-node";
                }

                string fullName = node.TypeName;
                string shortName = ShortDisplayName(fullName);

                svg.AppendLine($"  <g class=\"node-group\" data-address=\"0x{node.Address:x}\" data-fullname=\"{EscapeXml(fullName)}\" tabindex=\"0\">\n    <title>{EscapeXml(fullName)} (0x{node.Address:x})</title>");
                svg.AppendLine($"    <rect x=\"{pos.X}\" y=\"{pos.Y}\" width=\"{_nodeWidth}\" height=\"{_nodeHeight}\" class=\"{nodeClass}\" rx=\"5\"/>");
                svg.AppendLine($"    <text x=\"{pos.X + _nodeWidth / 2}\" y=\"{pos.Y + 18}\" class=\"node-text\" text-anchor=\"middle\">{EscapeXml(shortName)}</text>");
                svg.AppendLine($"    <text x=\"{pos.X + _nodeWidth / 2}\" y=\"{pos.Y + 32}\" class=\"node-address\" text-anchor=\"middle\">0x{node.Address:x}</text>");
                
                // Draw reference count badge if > 1
                if (node.ReferenceCount > 1)
                {
                    int badgeX = pos.X + _nodeWidth - 15;
                    int badgeY = pos.Y - 8;
                    svg.AppendLine($"    <circle cx=\"{badgeX}\" cy=\"{badgeY}\" r=\"12\" class=\"count-badge\"/>");
                    svg.AppendLine($"    <text x=\"{badgeX}\" y=\"{badgeY + 4}\" class=\"count-text\" text-anchor=\"middle\">{node.ReferenceCount}</text>");
                }
                svg.AppendLine("  </g>");
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

        // Add copy script for double-click
        svg.AppendLine("<script><![CDATA[");
        svg.AppendLine("(function(){\n  function copy(text){ if(navigator.clipboard){ navigator.clipboard.writeText(text).catch(()=>{}); } else { var ta=document.createElement('textarea'); ta.value=text; document.body.appendChild(ta); ta.select(); try{document.execCommand('copy');}catch(e){} document.body.removeChild(ta);} }\n  var groups=document.querySelectorAll('.node-group');\n  groups.forEach(function(g){\n    g.addEventListener('dblclick', function(){\n      var full=g.getAttribute('data-fullname');\n      var addr=g.getAttribute('data-address');\n      var payload=full+' '+addr;\n      copy(payload);\n      var rect=g.querySelector('rect');\n      if(rect){\n        rect.classList.add('copy-flash');\n        setTimeout(function(){rect.classList.remove('copy-flash');},1000);\n      }\n    });\n  });\n})();");
        svg.AppendLine("]]></script>");

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

    private string ShortDisplayName(string typeName)
    {
        if (string.IsNullOrEmpty(typeName)) return typeName;
        // Root kind labels like [Strong] should remain untouched
        if (typeName.StartsWith("[") && typeName.EndsWith("]")) return typeName;
        var lastDot = typeName.LastIndexOf('.');
        if (lastDot < 0) return TruncateText(typeName, 28);
        var tail = typeName.Substring(lastDot + 1);
        var display = ".." + tail;
        return TruncateText(display, 28);
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
