using Everywhere.Web;

namespace Everywhere.Core.Tests.Web;

public sealed class WebAccessibilityMarkdownConverterTests
{
    private static readonly Uri TestUri = new("https://example.com/page");

    [Test]
    public void EmptyTreeReturnsEmptyString()
    {
        var markdown = WebAccessibilityMarkdownConverter.Convert(TestUri, []);

        Assert.That(markdown, Is.Empty);
    }

    [Test]
    public void ConvertsHeadingParagraphListLinkImageCodeAndTable()
    {
        var nodes = new[]
        {
            Node("root", "RootWebArea", children: ["heading", "paragraph", "list", "image", "code", "table"]),
            Node("heading", "heading", "Important Page", level: 2),
            Node("paragraph", "paragraph", children: ["text", "link"]),
            Node("text", "StaticText", "Read the "),
            Node("link", "link", "documentation", url: "https://example.com/docs"),
            Node("list", "list", children: ["item1", "item2"]),
            Node("item1", "listitem", children: ["item1Text"]),
            Node("item1Text", "StaticText", "First item"),
            Node("item2", "listitem", children: ["item2Text"]),
            Node("item2Text", "StaticText", "Second item"),
            Node("image", "image", "Architecture", url: "https://example.com/diagram.png"),
            Node("code", "code", children: ["codeText"]),
            Node("codeText", "StaticText", "var x = 1;"),
            Node("table", "table", children: ["row1", "row2"]),
            Node("row1", "row", children: ["h1", "h2"]),
            Node("h1", "columnheader", children: ["h1Text"]),
            Node("h1Text", "StaticText", "Name"),
            Node("h2", "columnheader", children: ["h2Text"]),
            Node("h2Text", "StaticText", "Value"),
            Node("row2", "row", children: ["c1", "c2"]),
            Node("c1", "cell", children: ["c1Text"]),
            Node("c1Text", "StaticText", "Answer"),
            Node("c2", "cell", children: ["c2Text"]),
            Node("c2Text", "StaticText", "42")
        };

        var markdown = WebAccessibilityMarkdownConverter.Convert(TestUri, nodes);

        Assert.Multiple(() =>
        {
            Assert.That(markdown, Does.Contain("## Important Page"));
            Assert.That(markdown, Does.Contain("[documentation](https://example.com/docs)"));
            Assert.That(markdown, Does.Contain("- First item"));
            Assert.That(markdown, Does.Contain("![Architecture](https://example.com/diagram.png)"));
            Assert.That(markdown, Does.Contain("`var x = 1;`"));
            Assert.That(markdown, Does.Contain("| Name | Value |"));
            Assert.That(markdown, Does.Contain("| Answer | 42 |"));
        });
    }

    [Test]
    public void SkipsNavigationAndSamePageAnchorNoise()
    {
        var nodes = new[]
        {
            Node("root", "RootWebArea", children: ["nav", "main"]),
            Node("nav", "navigation", children: ["navText"]),
            Node("navText", "StaticText", "Home Pricing Login"),
            Node("main", "paragraph", children: ["mainText", "anchor"]),
            Node("mainText", "StaticText", "Useful content"),
            Node("anchor", "link", "section", url: "https://example.com/page#section")
        };

        var markdown = WebAccessibilityMarkdownConverter.Convert(TestUri, nodes);

        Assert.Multiple(() =>
        {
            Assert.That(markdown, Does.Contain("Useful content"));
            Assert.That(markdown, Does.Contain("section"));
            Assert.That(markdown, Does.Not.Contain("Home Pricing Login"));
            Assert.That(markdown, Does.Not.Contain("[section]"));
        });
    }

    private static WebAccessibilityNode Node(
        string id,
        string role,
        string? name = null,
        IReadOnlyList<string>? children = null,
        string? url = null,
        int? level = null,
        bool ignored = false) =>
        new()
        {
            NodeId = id,
            Role = role,
            Name = name,
            ChildIds = children ?? [],
            Url = url,
            Level = level,
            Ignored = ignored
        };
}
