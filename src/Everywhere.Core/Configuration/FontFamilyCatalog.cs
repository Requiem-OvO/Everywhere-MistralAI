using System.Globalization;
using System.Text;
using Avalonia.Media;
using Microsoft.Extensions.Logging;
using ZLinq;

namespace Everywhere.Configuration;

/// <summary>
/// Provides a stable, process-lifetime snapshot of the font families exposed by Avalonia.
/// </summary>
/// <remarks>
/// The snapshot contains only family-level values. Extended names are loaded once, on demand,
/// through Avalonia's public font APIs and never during filtering.
/// </remarks>
public sealed class FontFamilyCatalog
{
    /// <summary>
    /// Describes the state of an item's lazy metadata lookup.
    /// </summary>
    private enum MetadataState
    {
        NotLoaded,
        Loading,
        Loaded,
        Failed,
    }

    /// <summary>
    /// Represents the result of loading metadata for one font family.
    /// </summary>
    public sealed record Metadata(
        bool IsAvailable,
        string? TypographicFamilyName,
        IReadOnlyList<string> SearchNames
    )
    {
        /// <summary>
        /// Gets a failed metadata result.
        /// </summary>
        public static Metadata Unavailable { get; } = new(false, null, []);
    }

    /// <summary>
    /// Represents one selectable font family.
    /// </summary>
    public abstract class Item
    {
        /// <summary>
        /// Gets the stable value stored in settings.
        /// </summary>
        public string FontFamilyName { get; }

        /// <summary>
        /// Gets the font family returned by Avalonia's system font collection.
        /// </summary>
        public FontFamily FontFamily { get; }

        /// <summary>
        /// Gets the user-facing family name shown by the picker.
        /// </summary>
        public string DisplayName { get; }

        /// <summary>
        /// Gets whether this item represents a configured font that is not installed.
        /// </summary>
        public bool IsMissing { get; }

        /// <summary>
        /// Gets the family used to render the preview. Failed and missing fonts use the default family.
        /// </summary>
        public FontFamily PreviewFontFamily
        {
            get
            {
                EnsureMetadata();
                return _previewFontFamily;
            }
        }

        /// <summary>
        /// IsMissing or Failed for UI binding
        /// </summary>
        public bool HasWarning => IsMissing || _metadataState == MetadataState.Failed;

        /// <summary>
        /// Gets the single tooltip text for this item.
        /// </summary>
        public string? ToolTip
        {
            get
            {
                if (IsMissing) return LocaleResolver.FontFamilyPicker_NotInstalled;

                EnsureMetadata();
                return _metadataState switch
                {
                    MetadataState.Failed => LocaleResolver.FontFamilyPicker_PreviewUnavailable,
                    MetadataState.Loaded when !string.Equals(
                        _typographicFamilyName,
                        DisplayName,
                        StringComparison.OrdinalIgnoreCase) => _typographicFamilyName,
                    _ => null,
                };
            }
        }

        private readonly FontFamilyCatalog? _catalog;
        private string[] _normalizedSearchNames;
        private FontFamily _previewFontFamily;
        private string? _typographicFamilyName;
        private MetadataState _metadataState;

        /// <summary>
        /// Initializes a catalog item.
        /// </summary>
        protected Item(FontFamilyCatalog? catalog, string fontFamilyName, FontFamily fontFamily, string displayName, bool isMissing = false)
        {
            _catalog = catalog;
            FontFamilyName = fontFamilyName.Trim();
            FontFamily = fontFamily;
            DisplayName = displayName.Trim();
            IsMissing = isMissing;
            _previewFontFamily = isMissing ? FontFamily.Default : fontFamily;

            _normalizedSearchNames = NormalizeNames([fontFamilyName, displayName]);
            _metadataState = isMissing ? MetadataState.Failed : MetadataState.NotLoaded;
        }

        /// <summary>
        /// Determines whether any cached name contains the normalized query.
        /// </summary>
        public bool Matches(string normalizedQuery)
        {
            return normalizedQuery.Length == 0 ||
                _normalizedSearchNames.AsValueEnumerable().Any(name => name.Contains(normalizedQuery, StringComparison.Ordinal));
        }

        protected bool TryBeginMetadataLoad()
        {
            if (_metadataState != MetadataState.NotLoaded) return false;

            _metadataState = MetadataState.Loading;
            return true;
        }

        protected void CompleteMetadata(Metadata metadata)
        {
            var seen = new HashSet<string>(_normalizedSearchNames, StringComparer.Ordinal);
            var normalizedSearchNames = new List<string>(_normalizedSearchNames);

            foreach (var value in metadata.SearchNames.AsValueEnumerable())
            {
                var normalizedName = NormalizeSearchText(value);
                if (normalizedName.Length == 0 || !seen.Add(normalizedName)) continue;

                normalizedSearchNames.Add(normalizedName);
            }

            _typographicFamilyName = string.IsNullOrWhiteSpace(metadata.TypographicFamilyName) ? null : metadata.TypographicFamilyName.Trim();
            _normalizedSearchNames = normalizedSearchNames.ToArray();
            _metadataState = MetadataState.Loaded;
        }

        protected void FailMetadataLoad()
        {
            _previewFontFamily = FontFamily.Default;
            _metadataState = MetadataState.Failed;
        }

        private void EnsureMetadata() => _catalog?.EnsureMetadata(this);
    }

    private sealed class CatalogItem(
        FontFamilyCatalog? catalog,
        string fontFamilyName,
        FontFamily fontFamily,
        string displayName,
        bool isMissing = false
    ) : Item(catalog, fontFamilyName, fontFamily, displayName, isMissing)
    {
        public bool BeginMetadataLoad() => TryBeginMetadataLoad();

        public void PublishMetadata(Metadata metadata) => CompleteMetadata(metadata);

        public void PublishMetadataFailure() => FailMetadataLoad();
    }

    private readonly ILogger<FontFamilyCatalog> _logger;
    private readonly Lazy<Snapshot> _snapshot;

    /// <summary>
    /// Initializes a catalog. System fonts are not enumerated until <see cref="Items"/> is first read.
    /// </summary>
    public FontFamilyCatalog(ILogger<FontFamilyCatalog> logger)
    {
        _logger = logger;
        _snapshot = new Lazy<Snapshot>(CreateSnapshot, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    /// <summary>
    /// Gets the sorted, de-duplicated system font snapshot.
    /// </summary>
    public IReadOnlyList<Item> Items => _snapshot.Value.Items;

    /// <summary>
    /// Finds an installed family by its stable name, ignoring case.
    /// </summary>
    public Item? Find(string? fontFamilyName)
    {
        return string.IsNullOrWhiteSpace(fontFamilyName) ? null : _snapshot.Value.ByName.GetValueOrDefault(fontFamilyName.Trim());
    }

    /// <summary>
    /// Creates a temporary item for a configured family that is not present in the snapshot.
    /// </summary>
    public static Item CreateMissingItem(string fontFamilyName, string displayName) =>
        new CatalogItem(null, fontFamilyName, FontFamily.Default, displayName, isMissing: true);

    /// <summary>
    /// Loads and caches an item's metadata. Failures are contained and never retried.
    /// </summary>
    public void EnsureMetadata(Item item)
    {
        if (item is not CatalogItem catalogItem)
        {
            throw new ArgumentException("The item does not belong to this catalog.", nameof(item));
        }

        if (!catalogItem.BeginMetadataLoad()) return;

        try
        {
            var metadata = ReadMetadata(item.FontFamily);
            if (metadata.IsAvailable)
            {
                catalogItem.PublishMetadata(metadata);
                return;
            }

            catalogItem.PublishMetadataFailure();
            _logger.LogWarning("Could not load a glyph typeface for font family {FontFamilyName}.", item.FontFamilyName);
        }
        catch (Exception exception)
        {
            catalogItem.PublishMetadataFailure();
            _logger.LogWarning(exception, "Could not load metadata for font family {FontFamilyName}.", item.FontFamilyName);
        }
    }

    /// <summary>
    /// Normalizes user input for case- and diacritic-insensitive matching.
    /// </summary>
    public static string NormalizeSearchText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;

        var decomposed = value.Trim().Normalize(NormalizationForm.FormD);
        var result = new StringBuilder(decomposed.Length);
        var previousWasWhitespace = false;

        foreach (var character in decomposed.AsValueEnumerable())
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(character);
            if (category is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark)
            {
                continue;
            }

            if (char.IsWhiteSpace(character))
            {
                if (!previousWasWhitespace) result.Append(' ');
                previousWasWhitespace = true;
                continue;
            }

            result.Append(char.ToUpperInvariant(character));
            previousWasWhitespace = false;
        }

        return result.ToString().Normalize(NormalizationForm.FormC);
    }

    /// <summary>
    /// Reads names from a directly resolved glyph typeface without using fallback-based probing.
    /// </summary>
    private static Metadata ReadMetadata(FontFamily fontFamily)
    {
        if (!FontManager.Current.SystemFonts.TryGetGlyphTypeface(
                fontFamily.Name,
                FontStyle.Normal,
                FontWeight.Normal,
                FontStretch.Normal,
                out var glyphTypeface))
        {
            return Metadata.Unavailable;
        }

        var familyNames = new List<string>
        {
            fontFamily.Name,
            glyphTypeface.FamilyName,
            glyphTypeface.TypographicFamilyName,
        };
        familyNames.AddRange(glyphTypeface.FamilyNames.Values);

        var distinctFamilyNames = familyNames
            .AsValueEnumerable()
            .Where(static name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var searchNames = new List<string>(distinctFamilyNames);
        foreach (var faceName in glyphTypeface.FaceNames.Values.Where(static name => !string.IsNullOrWhiteSpace(name)))
        {
            searchNames.AddRange(distinctFamilyNames.Select(familyName => $"{familyName} {faceName}"));
        }

        return new Metadata(true, glyphTypeface.TypographicFamilyName, searchNames);
    }

    private Snapshot CreateSnapshot()
    {
        try
        {
            var items = FontManager.Current.SystemFonts
                .AsValueEnumerable()
                .Where(static family => !string.IsNullOrWhiteSpace(family.Name))
                .GroupBy(static family => family.Name, StringComparer.OrdinalIgnoreCase)
                .Select(static group => group.First())
                .Select(family => new CatalogItem(this, family.Name, family, family.Name))
                .OrderBy(static item => item.DisplayName, StringComparer.CurrentCultureIgnoreCase)
                .ToArray();

            return new Snapshot(
                items,
                items.ToDictionary<CatalogItem, string, Item>(
                    static item => item.FontFamilyName,
                    static item => item,
                    StringComparer.OrdinalIgnoreCase));
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Could not enumerate system font families.");
            return new Snapshot([], new Dictionary<string, Item>(StringComparer.OrdinalIgnoreCase));
        }
    }

    private static string[] NormalizeNames(IEnumerable<string> names) =>
        [.. names.Select(NormalizeSearchText).Where(static name => name.Length > 0).Distinct(StringComparer.Ordinal)];

    private sealed record Snapshot(IReadOnlyList<Item> Items, IReadOnlyDictionary<string, Item> ByName);
}