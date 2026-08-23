using System.Collections.ObjectModel;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Controls.Metadata;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Input;
using CommunityToolkit.Mvvm.Input;
using Everywhere.Configuration;

namespace Everywhere.Views;

/// <summary>
/// Selects one installed Avalonia font family while keeping free-form search text out of settings.
/// </summary>
[TemplatePart(AutoCompleteBoxPartName, typeof(AutoCompleteBox), IsRequired = true)]
public sealed partial class FontFamilyPicker : TemplatedControl
{
    private const string AutoCompleteBoxPartName = "PART_AutoCompleteBox";

    /// <summary>
    /// Defines the <see cref="SelectedFontFamilyName"/> property.
    /// </summary>
    public static readonly StyledProperty<string?> SelectedFontFamilyNameProperty =
        AvaloniaProperty.Register<FontFamilyPicker, string?>(
            nameof(SelectedFontFamilyName),
            defaultBindingMode: BindingMode.TwoWay,
            enableDataValidation: true);

    /// <summary>
    /// Gets or sets the stable Avalonia font family name stored in settings.
    /// </summary>
    /// <remarks>
    /// A null or empty value means that the application's default UI font should be used.
    /// Search text is never assigned to this property unless it corresponds to a selected catalog item.
    /// </remarks>
    public string? SelectedFontFamilyName
    {
        get => GetValue(SelectedFontFamilyNameProperty);
        set => SetValue(SelectedFontFamilyNameProperty, value);
    }

    private readonly FontFamilyCatalog _catalog;
    private readonly ObservableCollection<FontFamilyCatalog.Item> _items;
    private AutoCompleteBox? _autoCompleteBox;
    private FontFamilyCatalog.Item? _committedItem;
    private FontFamilyCatalog.Item? _missingItem;
    private string? _lastFilterText;
    private string _normalizedFilterText = string.Empty;
    private bool _isSynchronizing;
    private bool _cancelPendingSelection;

    /// <summary>
    /// Initializes a font family picker backed by the shared font catalog.
    /// </summary>
    public FontFamilyPicker(FontFamilyCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        _catalog = catalog;
        _items = [.. catalog.Items];
    }

    /// <inheritdoc/>
    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        if (_autoCompleteBox is not null)
        {
            _autoCompleteBox.SelectionChanged -= HandleSelectionChanged;
            _autoCompleteBox.DropDownClosed -= HandleDropDownClosed;
            _autoCompleteBox.KeyDown -= HandleKeyDown;
            _autoCompleteBox.LostFocus -= HandleLostFocus;
        }

        base.OnApplyTemplate(e);

        _autoCompleteBox = e.NameScope.Find<AutoCompleteBox>(AutoCompleteBoxPartName);
        if (_autoCompleteBox is null) return;

        _autoCompleteBox.ItemsSource = _items;
        _autoCompleteBox.ValueMemberBinding = CompiledBinding.Create((FontFamilyCatalog.Item i) => i.DisplayName);
        _autoCompleteBox.ItemFilter = FilterItem;
        _autoCompleteBox.SelectionChanged += HandleSelectionChanged;
        _autoCompleteBox.DropDownClosed += HandleDropDownClosed;
        _autoCompleteBox.KeyDown += HandleKeyDown;
        _autoCompleteBox.LostFocus += HandleLostFocus;

        SynchronizeEditor();
    }

    /// <inheritdoc/>
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == SelectedFontFamilyNameProperty && !_isSynchronizing)
        {
            ApplyExternalValue(change.GetNewValue<string?>());
        }
    }

    /// <inheritdoc/>
    protected override void UpdateDataValidation(AvaloniaProperty property, BindingValueType state, Exception? error)
    {
        if (property == SelectedFontFamilyNameProperty && _autoCompleteBox is not null)
        {
            DataValidationErrors.SetError(_autoCompleteBox, error);
        }
    }

    private bool FilterItem(string? searchText, object? value)
    {
        if (value is not FontFamilyCatalog.Item item) return false;

        if (!string.Equals(searchText, _lastFilterText, StringComparison.Ordinal))
        {
            _lastFilterText = searchText;
            _normalizedFilterText = FontFamilyCatalog.NormalizeSearchText(searchText);
        }

        return item.Matches(_normalizedFilterText);
    }

    [RelayCommand]
    private void Expand()
    {
        if (_autoCompleteBox is not { IsDropDownOpen: false }) return;
        _autoCompleteBox.SetCurrentValue(AutoCompleteBox.SelectedItemProperty, null);
        _autoCompleteBox.SetCurrentValue(AutoCompleteBox.TextProperty, string.Empty);
        _autoCompleteBox.SetCurrentValue(AutoCompleteBox.IsDropDownOpenProperty, true);
    }

    private void HandleSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_isSynchronizing || _autoCompleteBox?.SelectedItem is not FontFamilyCatalog.Item item) return;
        ToolTip.SetTip(_autoCompleteBox, item.ToolTip);
    }

    private void HandleDropDownClosed(object? sender, EventArgs e)
    {
        if (_cancelPendingSelection)
        {
            _cancelPendingSelection = false;
            RestoreCommittedSelection();
            return;
        }

        CommitCurrentSelectionOrRestore();
    }

    private void HandleKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape when _autoCompleteBox?.IsDropDownOpen == false:
                _cancelPendingSelection = false;
                RestoreCommittedSelection();
                break;
            case Key.Escape:
                _cancelPendingSelection = true;
                break;
            case Key.Enter when _autoCompleteBox?.IsDropDownOpen == false:
                CommitCurrentSelectionOrRestore();
                break;
        }
    }

    private void HandleLostFocus(object? sender, FocusChangedEventArgs e)
    {
        if (!IsKeyboardFocusWithin) CommitCurrentSelectionOrRestore();
    }

    private void ApplyExternalValue(string? value)
    {
        RemoveMissingItem();

        if (string.IsNullOrWhiteSpace(value))
        {
            _committedItem = null;
            SynchronizeEditor();
            return;
        }

        var fontFamilyName = value.Trim();
        var item = _catalog.Find(fontFamilyName);
        if (item is null)
        {
            var displayName = string.Format(CultureInfo.CurrentCulture, LocaleResolver.FontFamilyPicker_NotInstalledFormat, fontFamilyName);
            item = FontFamilyCatalog.CreateMissingItem(fontFamilyName, displayName);
            _missingItem = item;
            _items.Insert(0, item);
        }

        _committedItem = item;
        SynchronizeEditor();
    }

    private void CommitCurrentSelectionOrRestore()
    {
        if (_isSynchronizing || _autoCompleteBox is null) return;

        if (_autoCompleteBox.SelectedItem is FontFamilyCatalog.Item item &&
            string.Equals(_autoCompleteBox.Text?.Trim(), item.DisplayName, StringComparison.CurrentCultureIgnoreCase))
        {
            Commit(item);
        }
        else
        {
            RestoreCommittedSelection();
        }
    }

    private void Commit(FontFamilyCatalog.Item item)
    {
        if (!item.IsMissing) RemoveMissingItem();

        _committedItem = item;

        _isSynchronizing = true;
        try
        {
            SetCurrentValue(SelectedFontFamilyNameProperty, item.FontFamilyName);
            SynchronizeEditorCore();
        }
        finally
        {
            _isSynchronizing = false;
        }
    }

    private void RestoreCommittedSelection() => SynchronizeEditor();

    private void SynchronizeEditor()
    {
        _isSynchronizing = true;
        try
        {
            SynchronizeEditorCore();
        }
        finally
        {
            _isSynchronizing = false;
        }
    }

    private void SynchronizeEditorCore()
    {
        if (_autoCompleteBox is null) return;

        _autoCompleteBox.SetCurrentValue(AutoCompleteBox.SelectedItemProperty, _committedItem);
        _autoCompleteBox.SetCurrentValue(AutoCompleteBox.TextProperty, _committedItem?.DisplayName ?? string.Empty);
        ToolTip.SetTip(_autoCompleteBox, _committedItem?.ToolTip);
    }

    private void RemoveMissingItem()
    {
        if (_missingItem is null) return;
        _items.Remove(_missingItem);
        _missingItem = null;
    }
}