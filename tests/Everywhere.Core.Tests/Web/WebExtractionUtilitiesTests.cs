using Everywhere.Web;

namespace Everywhere.Core.Tests.Web;

public sealed class WebExtractionUtilitiesTests
{
    [Test]
    public void SelectBestFallbackReturnsLongestNonEmptyCandidate()
    {
        var candidates = new[]
        {
            WebPageExtractionResult.Create("short", "Short", WebExtractionSource.AccessibilityTree),
            WebPageExtractionResult.Create("This candidate has much more useful content.", "Long", WebExtractionSource.CleanedBody),
            WebPageExtractionResult.Create(" ", "Empty", WebExtractionSource.Readability)
        };

        var result = WebExtractionUtilities.SelectBestFallback(candidates);

        Assert.That(result?.Source, Is.EqualTo(WebExtractionSource.CleanedBody));
    }

    [Test]
    public void SelectBestCandidateRanksCleanerCandidateOverNoisyFirstCandidate()
    {
        var noisyFirst = string.Join(
            "\n",
            Enumerable.Repeat("[Home](https://example.com) [Docs](https://example.com/docs)", 80));
        var cleanerLater = Article(
            "Branch prediction matters",
            "Modern processors build a prediction history and speculatively execute instructions. " +
            "When the data is ordered, the branch predictor can learn the pattern and avoid repeated pipeline flushes.");
        var candidates = new[]
        {
            WebPageExtractionResult.Create(noisyFirst, "AX", WebExtractionSource.AccessibilityTree),
            WebPageExtractionResult.Create(cleanerLater, "Readability", WebExtractionSource.Readability)
        };

        var result = WebExtractionUtilities.SelectBestCandidate(candidates);

        Assert.Multiple(() =>
        {
            Assert.That(result?.Source, Is.EqualTo(WebExtractionSource.Readability));
            Assert.That(result?.Selection?.Scores, Has.Count.EqualTo(2));
        });
    }

    [Test]
    public void LongNoisyCleanedBodyDoesNotBeatCleanerCandidate()
    {
        var clean = Article(
            "C# tour",
            "C# is a type-safe object-oriented language. Programs are built from types and members, " +
            "and the language supports generics, pattern matching, asynchronous workflows, and LINQ queries.");
        var noisyBody = string.Join(
            "\n",
            Enumerable.Repeat("[Navigation](https://example.com/navigation)", 500));
        var candidates = new[]
        {
            WebPageExtractionResult.Create(clean, "Readability", WebExtractionSource.Readability),
            WebPageExtractionResult.Create(noisyBody, "Body", WebExtractionSource.CleanedBody)
        };

        var result = WebExtractionUtilities.SelectBestCandidate(candidates);

        Assert.That(result?.Source, Is.EqualTo(WebExtractionSource.Readability));
    }

    [Test]
    public void RankCandidatesUsesSourcePriorWhenScoresAreClose()
    {
        var content = Article(
            "Shared article",
            "The same clean article appears in two extraction candidates with identical structure and wording.");
        var candidates = new[]
        {
            WebPageExtractionResult.Create(content, "Readability", WebExtractionSource.Readability),
            WebPageExtractionResult.Create(content, "AX", WebExtractionSource.AccessibilityTree)
        };

        var result = WebExtractionUtilities.SelectBestCandidate(candidates);

        Assert.That(result?.Source, Is.EqualTo(WebExtractionSource.AccessibilityTree));
    }

    [Test]
    public void ConsensusScoreRewardsCandidatesSharingCoreText()
    {
        var shared = Article(
            "Sorted arrays and branch prediction",
            "The sorted input lets the branch predictor settle into a stable pattern. " +
            "The unsorted input forces frequent prediction misses, which cost many cycles.");
        var unrelated = Article(
            "Navigation",
            "Settings profile account billing preferences keyboard shortcuts appearance themes extensions marketplace.");
        var candidates = new[]
        {
            WebPageExtractionResult.Create(unrelated, "AX", WebExtractionSource.AccessibilityTree),
            WebPageExtractionResult.Create(shared, "Readability", WebExtractionSource.Readability),
            WebPageExtractionResult.Create(shared + "\n\nAdditional details about cache locality and measurements.", "DOM", WebExtractionSource.DomMainElement)
        };

        var selection = WebExtractionUtilities.RankCandidates(candidates);
        Assert.That(selection, Is.Not.Null);
        var readability = selection.Value.Scores.Single(score => score.Source == WebExtractionSource.Readability);
        var accessibility = selection.Value.Scores.Single(score => score.Source == WebExtractionSource.AccessibilityTree);

        Assert.Multiple(() =>
        {
            Assert.That(readability.Diagnostics.ConsensusScore, Is.GreaterThan(accessibility.Diagnostics.ConsensusScore));
            Assert.That(readability.Score, Is.GreaterThan(accessibility.Score));
        });
    }

    [Test]
    public void SelectBestCandidateReturnsShortNonEmptyCandidateWithLowConfidence()
    {
        var candidates = new[]
        {
            WebPageExtractionResult.Create("short", "AX", WebExtractionSource.AccessibilityTree),
            WebPageExtractionResult.Create("## Short\n\nsmall body", "DOM", WebExtractionSource.DomMainElement)
        };

        var result = WebExtractionUtilities.SelectBestCandidate(candidates);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.Not.Null);
            Assert.That(result?.Selection?.Confidence, Is.LessThanOrEqualTo(0.35));
        });
    }

    [Test]
    public void SelectBestCandidateReturnsNullWhenAllCandidatesAreEmpty()
    {
        var candidates = new[]
        {
            WebPageExtractionResult.Create(" ", "AX", WebExtractionSource.AccessibilityTree),
            WebPageExtractionResult.Create("", "DOM", WebExtractionSource.DomMainElement)
        };

        var result = WebExtractionUtilities.SelectBestCandidate(candidates);

        Assert.That(result, Is.Null);
    }

    [Test]
    public void RankCandidatesScoresOnlyBoundedSample()
    {
        var longContent = Article("Long", "Useful opening paragraph.") + "\n" + new string('x', 400_000);
        var candidates = new[]
        {
            WebPageExtractionResult.Create(longContent, "Body", WebExtractionSource.CleanedBody)
        };

        var selection = WebExtractionUtilities.RankCandidates(candidates);
        var score = selection?.Scores.Single();

        Assert.That(score?.Diagnostics.SampleLength, Is.LessThanOrEqualTo(256 * 1024));
    }

    [Test]
    public void ConvertHtmlToMarkdownRemovesCommentsAndKeepsLinks()
    {
        var html =
            """
            <!-- remove me -->
            <article>
              <h1>Title</h1>
              <p>Read <a href="https://example.com">more</a>.</p>
            </article>
            """;

        var markdown = WebExtractionUtilities.ConvertHtmlToMarkdown(html);

        Assert.Multiple(() =>
        {
            Assert.That(markdown, Does.Contain("Title"));
            Assert.That(markdown, Does.Contain("[more](https://example.com)"));
            Assert.That(markdown, Does.Not.Contain("remove me"));
        });
    }

    [Test]
    public void HasEnoughContentCountsNonWhitespaceCharacters()
    {
        var enough = new string('a', WebExtractionUtilities.MinimumContentLength);
        var tooShort = string.Join(' ', Enumerable.Repeat("a", WebExtractionUtilities.MinimumContentLength - 1));

        Assert.Multiple(() =>
        {
            Assert.That(WebExtractionUtilities.HasEnoughContent(enough), Is.True);
            Assert.That(WebExtractionUtilities.HasEnoughContent(tooShort), Is.False);
        });
    }

    [Test]
    public void BuildDocumentRequestHeadersOverridesAcceptAndKeepsExistingHeaders()
    {
        var original = new Dictionary<string, string>
        {
            ["accept"] = "text/html",
            ["User-Agent"] = "TestAgent"
        };

        var headers = WebExtractionUtilities.BuildDocumentRequestHeaders(original);

        Assert.Multiple(() =>
        {
            Assert.That(headers["Accept"], Is.EqualTo(WebExtractionUtilities.PreferredDocumentAcceptHeader));
            Assert.That(headers["DNT"], Is.EqualTo("1"));
            Assert.That(headers["Sec-GPC"], Is.EqualTo("1"));
            Assert.That(headers["User-Agent"], Is.EqualTo("TestAgent"));
            Assert.That(headers.Keys.Count(key => string.Equals(key, "accept", StringComparison.OrdinalIgnoreCase)), Is.EqualTo(1));
        });
    }

    [Test]
    public void SingleSelectionDescribesMarkdownResponseWithoutHtmlRanking()
    {
        var result = WebPageExtractionResult.Create("# Title\n\nMarkdown body", "Title", WebExtractionSource.MarkdownResponse);
        var selection = WebExtractionSelection.Single(result);

        Assert.Multiple(() =>
        {
            Assert.That(selection.SelectedSource, Is.EqualTo(WebExtractionSource.MarkdownResponse));
            Assert.That(selection.Scores, Has.Count.EqualTo(1));
            Assert.That(selection.Scores[0].Source, Is.EqualTo(WebExtractionSource.MarkdownResponse));
        });
    }

    private static string Article(string title, string paragraph) =>
        $"""
        # {title}

        {paragraph}

        - First important point with enough context to read like article content.
        - Second important point with supporting details and examples.

        ## Details

        {paragraph}
        """;
}
