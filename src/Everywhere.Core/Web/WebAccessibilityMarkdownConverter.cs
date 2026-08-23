using System.Text;
using System.Text.Json;
using ZLinq;

namespace Everywhere.Web;

internal sealed class WebAccessibilityNode
{
    public string NodeId { get; init; } = string.Empty;
    public IReadOnlyList<string> ChildIds { get; init; } = [];
    public bool Ignored { get; init; }
    public string? Role { get; init; }
    public string? Name { get; init; }
    public string? Value { get; init; }
    public string? Url { get; init; }
    public int? Level { get; init; }

    public static IReadOnlyList<WebAccessibilityNode> ParseNodes(JsonElement response)
    {
        if (!response.TryGetProperty("nodes", out var nodesElement) ||
            nodesElement.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var nodes = new List<WebAccessibilityNode>();
        foreach (var nodeElement in nodesElement.EnumerateArray())
        {
            nodes.Add(ParseNode(nodeElement));
        }

        return nodes;
    }

    private static WebAccessibilityNode ParseNode(JsonElement nodeElement)
    {
        var properties = ReadProperties(nodeElement);
        return new WebAccessibilityNode
        {
            NodeId = ReadString(nodeElement, "nodeId") ?? string.Empty,
            ChildIds = ReadStringArray(nodeElement, "childIds"),
            Ignored = ReadBool(nodeElement, "ignored"),
            Role = ReadValueObjectString(nodeElement, "role"),
            Name = ReadValueObjectString(nodeElement, "name"),
            Value = ReadValueObjectString(nodeElement, "value"),
            Url = properties.GetValueOrDefault("url"),
            Level = TryReadInt(properties.GetValueOrDefault("level"))
        };
    }

    private static Dictionary<string, string?> ReadProperties(JsonElement nodeElement)
    {
        var properties = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        if (!nodeElement.TryGetProperty("properties", out var propertiesElement) ||
            propertiesElement.ValueKind != JsonValueKind.Array)
        {
            return properties;
        }

        foreach (var propertyElement in propertiesElement.EnumerateArray())
        {
            var name = ReadString(propertyElement, "name");
            if (string.IsNullOrWhiteSpace(name)) continue;

            properties[name] = ReadValueObjectString(propertyElement, "value");
        }

        return properties;
    }

    private static string? ReadValueObjectString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var valueObject) ||
            valueObject.ValueKind != JsonValueKind.Object ||
            !valueObject.TryGetProperty("value", out var valueElement))
        {
            return null;
        }

        return valueElement.ValueKind switch
        {
            JsonValueKind.String => valueElement.GetString(),
            JsonValueKind.Number => valueElement.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => null
        };
    }

    private static string? ReadString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var valueElement) ||
            valueElement.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return valueElement.GetString();
    }

    private static bool ReadBool(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var valueElement) &&
        valueElement.ValueKind == JsonValueKind.True;

    private static IReadOnlyList<string> ReadStringArray(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var arrayElement) ||
            arrayElement.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var values = new List<string>();
        foreach (var item in arrayElement.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String && item.GetString() is { Length: > 0 } value)
            {
                values.Add(value);
            }
        }

        return values;
    }

    private static int? TryReadInt(string? value) =>
        int.TryParse(value, out var result) ? result : null;
}

internal static class WebAccessibilityMarkdownConverter
{
    private static readonly HashSet<string> NoiseRoles = new(StringComparer.OrdinalIgnoreCase)
    {
        "banner",
        "complementary",
        "contentinfo",
        "navigation",
        "search"
    };

    private static readonly HashSet<string> CellRoles = new(StringComparer.OrdinalIgnoreCase)
    {
        "cell",
        "columnheader",
        "gridcell",
        "rowheader"
    };

    public static string Convert(Uri pageUri, IReadOnlyList<WebAccessibilityNode> nodes)
    {
        if (nodes.Count == 0) return string.Empty;

        var context = new ConversionContext(pageUri, nodes);
        foreach (var root in context.Roots)
        {
            RenderBlock(context, root, indent: 0);
        }

        return WebExtractionUtilities.NormalizeMarkdown(context.Builder.ToString());
    }

    private static void RenderBlock(ConversionContext context, WebAccessibilityNode node, int indent)
    {
        if (context.IsVisited(node) || ShouldSkipNode(node)) return;
        context.MarkVisited(node);

        var role = node.Role ?? string.Empty;
        if (role.Equals("heading", StringComparison.OrdinalIgnoreCase))
        {
            var text = FirstNonEmpty(node.Name, CollectInlineText(context, node));
            AppendParagraph(context.Builder, $"{new string('#', Math.Clamp(node.Level ?? 2, 1, 6))} {text}");
            return;
        }

        if (role.Equals("paragraph", StringComparison.OrdinalIgnoreCase))
        {
            AppendWrappedParagraph(context.Builder, CollectInlineText(context, node));
            return;
        }

        if (role.Equals("list", StringComparison.OrdinalIgnoreCase))
        {
            RenderList(context, node, indent);
            return;
        }

        if (role.Equals("listitem", StringComparison.OrdinalIgnoreCase))
        {
            AppendWrappedParagraph(context.Builder, CollectInlineText(context, node));
            return;
        }

        if (role.Equals("pre", StringComparison.OrdinalIgnoreCase))
        {
            AppendCodeBlock(context.Builder, CollectInlineText(context, node));
            return;
        }

        if (role.Equals("code", StringComparison.OrdinalIgnoreCase))
        {
            var text = FirstNonEmpty(node.Name, CollectChildrenInlineText(context, node));
            if (text.Contains('\n'))
            {
                AppendCodeBlock(context.Builder, text);
            }
            else
            {
                AppendWrappedParagraph(context.Builder, $"`{EscapeBackticks(text)}`");
            }

            return;
        }

        if (role.Equals("table", StringComparison.OrdinalIgnoreCase))
        {
            RenderTable(context, node);
            return;
        }

        if (role.Equals("descriptionlist", StringComparison.OrdinalIgnoreCase))
        {
            RenderDescriptionList(context, node);
            return;
        }

        if (role.Equals("link", StringComparison.OrdinalIgnoreCase) ||
            role.Equals("image", StringComparison.OrdinalIgnoreCase) ||
            role.Equals("StaticText", StringComparison.OrdinalIgnoreCase))
        {
            AppendWrappedParagraph(context.Builder, RenderInline(context, node));
            return;
        }

        foreach (var child in context.GetChildren(node))
        {
            RenderBlock(context, child, indent);
        }
    }

    private static void RenderList(ConversionContext context, WebAccessibilityNode listNode, int indent)
    {
        foreach (var item in context.GetChildren(listNode).AsValueEnumerable().Where(static n => RoleEquals(n, "listitem")))
        {
            var text = CollectInlineText(
                context,
                item,
                child => !RoleEquals(child, "list") && !RoleEquals(child, "ListMarker"));
            if (!string.IsNullOrWhiteSpace(text))
            {
                context.Builder
                    .Append(' ', indent * 2)
                    .Append("- ")
                    .AppendLine(text);
            }

            foreach (var nestedList in context.GetChildren(item).AsValueEnumerable().Where(static n => RoleEquals(n, "list")))
            {
                RenderList(context, nestedList, indent + 1);
            }
        }

        context.Builder.AppendLine();
    }

    private static void RenderTable(ConversionContext context, WebAccessibilityNode tableNode)
    {
        var rows = context.GetChildren(tableNode)
            .AsValueEnumerable()
            .Where(static n => RoleEquals(n, "row"))
            .Select(row => context.GetChildren(row)
                .Where(static cell => CellRoles.Contains(cell.Role ?? string.Empty))
                .Select(cell => EscapeTableCell(CollectInlineText(context, cell)))
                .Where(static text => !string.IsNullOrWhiteSpace(text))
                .ToArray())
            .Where(static cells => cells.Length > 0)
            .ToArray();

        if (rows.Length == 0)
        {
            AppendWrappedParagraph(context.Builder, CollectInlineText(context, tableNode));
            return;
        }

        var columnCount = rows.AsValueEnumerable().Max(static row => row.Length);
        AppendTableRow(context.Builder, rows[0], columnCount);
        AppendTableRow(context.Builder, [.. Enumerable.Repeat("---", columnCount)], columnCount);
        foreach (var row in rows.AsValueEnumerable().Skip(1))
        {
            AppendTableRow(context.Builder, row, columnCount);
        }

        context.Builder.AppendLine();
    }

    private static void RenderDescriptionList(ConversionContext context, WebAccessibilityNode node)
    {
        foreach (var child in context.GetChildren(node).AsValueEnumerable())
        {
            var text = CollectInlineText(context, child);
            if (string.IsNullOrWhiteSpace(text)) continue;

            if (RoleEquals(child, "term"))
            {
                context.Builder.Append("**").Append(text).AppendLine("**");
            }
            else
            {
                context.Builder.AppendLine(text).AppendLine();
            }
        }
    }

    private static string CollectInlineText(
        ConversionContext context,
        WebAccessibilityNode node,
        Func<WebAccessibilityNode, bool>? childPredicate = null)
    {
        var text = RenderInline(context, node);
        if (!string.IsNullOrWhiteSpace(text)) return text;

        var builder = new StringBuilder();
        foreach (var child in context.GetChildren(node).AsValueEnumerable())
        {
            if (ShouldSkipNode(child)) continue;
            if (childPredicate is not null && !childPredicate(child)) continue;

            var childText = CollectInlineText(context, child, childPredicate);
            if (string.IsNullOrWhiteSpace(childText)) continue;

            if (builder.Length > 0 && NeedsSpace(builder[^1], childText[0]))
            {
                builder.Append(' ');
            }

            builder.Append(childText);
        }

        return builder.ToString().Trim();
    }

    private static string RenderInline(ConversionContext context, WebAccessibilityNode node)
    {
        if (ShouldSkipNode(node)) return string.Empty;

        if (RoleEquals(node, "StaticText") || RoleEquals(node, "InlineTextBox") || RoleEquals(node, "ListMarker"))
        {
            return node.Name?.Trim() ?? string.Empty;
        }

        if (RoleEquals(node, "link"))
        {
            var text = FirstNonEmpty(node.Name, CollectChildrenInlineText(context, node));
            if (string.IsNullOrWhiteSpace(text)) return string.Empty;
            if (string.IsNullOrWhiteSpace(node.Url) || IsSamePageLink(context.PageUri, node.Url))
            {
                return text;
            }

            return $"[{EscapeLinkText(text)}]({node.Url})";
        }

        if (RoleEquals(node, "image"))
        {
            var alt = FirstNonEmpty(node.Name, node.Value, "Image");
            if (string.IsNullOrWhiteSpace(node.Url)) return alt;

            return $"![{EscapeLinkText(alt)}]({node.Url})";
        }

        if (RoleEquals(node, "code"))
        {
            var text = FirstNonEmpty(node.Name, CollectChildrenInlineText(context, node));
            return string.IsNullOrWhiteSpace(text) ? string.Empty : $"`{EscapeBackticks(text)}`";
        }

        if (node.ChildIds.Count == 0)
        {
            return node.Name?.Trim() ?? node.Value?.Trim() ?? string.Empty;
        }

        return string.Empty;
    }

    private static string CollectChildrenInlineText(ConversionContext context, WebAccessibilityNode node)
    {
        var builder = new StringBuilder();
        foreach (var child in context.GetChildren(node).AsValueEnumerable())
        {
            var childText = CollectInlineText(context, child);
            if (string.IsNullOrWhiteSpace(childText)) continue;

            if (builder.Length > 0 && NeedsSpace(builder[^1], childText[0]))
            {
                builder.Append(' ');
            }

            builder.Append(childText);
        }

        return builder.ToString().Trim();
    }

    private static bool ShouldSkipNode(WebAccessibilityNode node) =>
        node.Ignored || NoiseRoles.Contains(node.Role ?? string.Empty);

    private static bool RoleEquals(WebAccessibilityNode node, string role) =>
        string.Equals(node.Role, role, StringComparison.OrdinalIgnoreCase);

    private static string FirstNonEmpty(params string?[] values) =>
        values.AsValueEnumerable().FirstOrDefault(static v => !string.IsNullOrWhiteSpace(v))?.Trim() ?? string.Empty;

    private static void AppendWrappedParagraph(StringBuilder builder, string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        AppendParagraph(builder, WrapParagraph(text.Trim()));
    }

    private static void AppendParagraph(StringBuilder builder, string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        builder.AppendLine(text.Trim()).AppendLine();
    }

    private static void AppendCodeBlock(StringBuilder builder, string code)
    {
        if (string.IsNullOrWhiteSpace(code)) return;
        builder.AppendLine("```").AppendLine(code.Trim()).AppendLine("```").AppendLine();
    }

    private static void AppendTableRow(StringBuilder builder, IReadOnlyList<string> row, int columnCount)
    {
        builder.Append('|');
        for (var i = 0; i < columnCount; i++)
        {
            builder.Append(' ').Append(i < row.Count ? row[i] : string.Empty).Append(" |");
        }

        builder.AppendLine();
    }

    private static string WrapParagraph(string text)
    {
        const int maxLineLength = 100;
        if (text.Length <= maxLineLength) return text;

        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var builder = new StringBuilder();
        var lineLength = 0;
        foreach (var word in words.AsValueEnumerable())
        {
            if (lineLength > 0 && lineLength + 1 + word.Length > maxLineLength)
            {
                builder.AppendLine();
                lineLength = 0;
            }
            else if (lineLength > 0)
            {
                builder.Append(' ');
                lineLength++;
            }

            builder.Append(word);
            lineLength += word.Length;
        }

        return builder.ToString();
    }

    private static string EscapeBackticks(string text) => text.Replace("`", "\\`");

    private static string EscapeLinkText(string text) =>
        text.Replace("[", "\\[").Replace("]", "\\]");

    private static string EscapeTableCell(string text) =>
        text.Replace("|", "\\|").ReplaceLineEndings(" ").Trim();

    private static bool NeedsSpace(char previous, char next) =>
        !char.IsWhiteSpace(previous) &&
        !char.IsWhiteSpace(next) &&
        !char.IsPunctuation(previous) &&
        next is not ('.' or ',' or ';' or ':' or ')' or ']' or '}');

    private static bool IsSamePageLink(Uri pageUri, string url)
    {
        if (url.StartsWith('#')) return true;
        if (!Uri.TryCreate(pageUri, url, out var targetUri)) return false;

        return string.Equals(pageUri.Scheme, targetUri.Scheme, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(pageUri.Host, targetUri.Host, StringComparison.OrdinalIgnoreCase) &&
            pageUri.Port == targetUri.Port &&
            string.Equals(pageUri.AbsolutePath, targetUri.AbsolutePath, StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrEmpty(targetUri.Fragment);
    }

    private sealed class ConversionContext
    {
        private readonly Dictionary<string, WebAccessibilityNode> _nodesById;
        private readonly HashSet<string> _visited = new(StringComparer.Ordinal);

        public ConversionContext(Uri pageUri, IReadOnlyList<WebAccessibilityNode> nodes)
        {
            PageUri = pageUri;
            Builder = new StringBuilder();
            _nodesById = nodes
                .Where(static node => !string.IsNullOrWhiteSpace(node.NodeId))
                .GroupBy(static node => node.NodeId, StringComparer.Ordinal)
                .ToDictionary(static group => group.Key, static group => group.First(), StringComparer.Ordinal);

            var childIds = nodes.SelectMany(static node => node.ChildIds).ToHashSet(StringComparer.Ordinal);
            var roots = nodes.Where(node => !childIds.Contains(node.NodeId)).ToArray();
            Roots = roots.Length > 0 ? roots : [nodes[0]];
        }

        public Uri PageUri { get; }
        public StringBuilder Builder { get; }
        public IReadOnlyList<WebAccessibilityNode> Roots { get; }

        public IEnumerable<WebAccessibilityNode> GetChildren(WebAccessibilityNode node)
        {
            foreach (var childId in node.ChildIds)
            {
                if (_nodesById.TryGetValue(childId, out var child))
                {
                    yield return child;
                }
            }
        }

        public bool IsVisited(WebAccessibilityNode node) =>
            !string.IsNullOrWhiteSpace(node.NodeId) && _visited.Contains(node.NodeId);

        public void MarkVisited(WebAccessibilityNode node)
        {
            if (!string.IsNullOrWhiteSpace(node.NodeId))
            {
                _visited.Add(node.NodeId);
            }
        }
    }
}

internal static class WebCdpFrameTreeParser
{
    public static IReadOnlyList<string> ParseFrameIds(JsonElement response)
    {
        if (!response.TryGetProperty("frameTree", out var frameTree) ||
            frameTree.ValueKind != JsonValueKind.Object)
        {
            return [];
        }

        var frameIds = new List<string>();
        CollectFrameIds(frameTree, frameIds);
        return frameIds;
    }

    private static void CollectFrameIds(JsonElement frameTree, List<string> frameIds)
    {
        if (frameTree.TryGetProperty("frame", out var frame) &&
            frame.ValueKind == JsonValueKind.Object &&
            frame.TryGetProperty("id", out var idElement) &&
            idElement.ValueKind == JsonValueKind.String &&
            idElement.GetString() is { Length: > 0 } id)
        {
            frameIds.Add(id);
        }

        if (!frameTree.TryGetProperty("childFrames", out var childFrames) ||
            childFrames.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var childFrame in childFrames.EnumerateArray())
        {
            CollectFrameIds(childFrame, frameIds);
        }
    }
}