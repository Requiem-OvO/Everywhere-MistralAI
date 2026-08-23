using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Text;
using System.Text.Json.Serialization;
using Everywhere.Collections;
using ZLinq;

namespace Everywhere.Utilities;

/// <summary>
/// Identifies the semantic kind of one DeepObserver path segment.
/// </summary>
public enum DeepObserverPathSegmentKind
{
    /// <summary>
    /// A CLR property name.
    /// </summary>
    Property,

    /// <summary>
    /// A zero-based index inside an observed list.
    /// </summary>
    CollectionIndex,

    /// <summary>
    /// A dictionary key represented by its CLR string form.
    /// </summary>
    DictionaryKey
}

/// <summary>
/// Represents one typed segment in an DeepObserver path.
/// </summary>
/// <remarks>
/// Property names and dictionary keys are kept as complete strings. In particular,
/// a colon in a dictionary key has no path-separator meaning. Collection indexes
/// remain integers so consumers do not need to allocate and parse index strings.
/// </remarks>
public readonly record struct DeepObserverPathSegment
{
    private DeepObserverPathSegment(DeepObserverPathSegmentKind kind, string? name, int index)
    {
        Kind = kind;
        Name = name;
        Index = index;
    }

    /// <summary>
    /// Gets the semantic kind of this segment.
    /// </summary>
    public DeepObserverPathSegmentKind Kind { get; }

    /// <summary>
    /// Gets the property name or dictionary key for a named segment.
    /// </summary>
    public string? Name { get; }

    /// <summary>
    /// Gets the collection index for an index segment.
    /// </summary>
    public int Index { get; }

    /// <summary>
    /// Creates a CLR property segment.
    /// </summary>
    public static DeepObserverPathSegment Property(string name) =>
        string.IsNullOrEmpty(name) ?
            throw new ArgumentException("DeepObserver property path segment cannot be null or empty.", nameof(name)) :
            new DeepObserverPathSegment(DeepObserverPathSegmentKind.Property, name, -1);

    /// <summary>
    /// Creates a collection index segment.
    /// </summary>
    public static DeepObserverPathSegment CollectionIndex(int index) =>
        index < 0 ?
            throw new ArgumentOutOfRangeException(nameof(index), index, "DeepObserver collection index cannot be negative.") :
            new DeepObserverPathSegment(DeepObserverPathSegmentKind.CollectionIndex, null, index);

    /// <summary>
    /// Creates a dictionary key segment.
    /// </summary>
    /// <remarks>
    /// Empty keys are valid dictionary keys and are therefore preserved.
    /// </remarks>
    public static DeepObserverPathSegment DictionaryKey(string key) =>
        key is null ?
            throw new ArgumentNullException(nameof(key)) :
            new DeepObserverPathSegment(DeepObserverPathSegmentKind.DictionaryKey, key, -1);

    /// <summary>
    /// Gets the name for a property or dictionary-key segment.
    /// </summary>
    public string GetName() =>
        (Kind is DeepObserverPathSegmentKind.Property or DeepObserverPathSegmentKind.DictionaryKey) && Name is not null ?
            Name :
            throw new InvalidOperationException("DeepObserver path segment does not contain a name.");
}

/// <summary>
/// Stores an immutable, allocation-conscious DeepObserver path.
/// </summary>
/// <remarks>
/// The path owns its backing array so an event can safely be retained after the
/// observer continues processing later notifications. Internal append operations
/// copy only the segment array and never concatenate or split delimiter strings.
/// </remarks>
public readonly struct DeepObserverPath : IEquatable<DeepObserverPath>
{
    private readonly DeepObserverPathSegment[]? _segments;

    /// <summary>
    /// Creates a path by copying the supplied segments.
    /// </summary>
    public DeepObserverPath(ReadOnlySpan<DeepObserverPathSegment> segments) => _segments = [.. segments];

    private DeepObserverPath(DeepObserverPathSegment[] segments)
    {
        _segments = segments;
    }

    /// <summary>
    /// Gets the root path.
    /// </summary>
    public static DeepObserverPath Root { get; } = new([]);

    /// <summary>
    /// Gets the number of segments in this path.
    /// </summary>
    public int Length => _segments?.Length ?? 0;

    /// <summary>
    /// Gets a segment by its zero-based position.
    /// </summary>
    public DeepObserverPathSegment this[int index] =>
        _segments is { } segments ? segments[index] : throw new IndexOutOfRangeException();

    /// <summary>
    /// Gets a read-only view over the path segments.
    /// </summary>
    public ReadOnlySpan<DeepObserverPathSegment> AsSpan() => _segments ?? [];

    public DeepObserverPath Append(DeepObserverPathSegment segment)
    {
        var segments = new DeepObserverPathSegment[Length + 1];
        AsSpan().CopyTo(segments);
        segments[^1] = segment;
        return new DeepObserverPath(segments);
    }

    public bool Equals(DeepObserverPath other) => AsSpan().SequenceEqual(other.AsSpan());

    public override bool Equals(object? obj) => obj is DeepObserverPath other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var segment in AsSpan())
        {
            hash.Add(segment);
        }

        return hash.ToHashCode();
    }

    public override string ToString()
    {
        var builder = new StringBuilder("$");
        foreach (var segment in AsSpan())
        {
            switch (segment.Kind)
            {
                case DeepObserverPathSegmentKind.Property when segment.Name is { } propertyName:
                    AppendNamedSegment(builder, propertyName, useBracket: propertyName.Any(static c => !char.IsLetterOrDigit(c) && c != '_'));
                    break;
                case DeepObserverPathSegmentKind.DictionaryKey when segment.Name is { } dictionaryKey:
                    builder.Append("[\"").Append(EscapeName(dictionaryKey)).Append("\"]");
                    break;
                case DeepObserverPathSegmentKind.CollectionIndex:
                    builder.Append('[').Append(segment.Index).Append(']');
                    break;
                default:
                    throw new InvalidOperationException("Invalid DeepObserver path segment.");
            }
        }

        return builder.ToString();

        static void AppendNamedSegment(StringBuilder builder, string name, bool useBracket)
        {
            if (useBracket)
            {
                builder.Append("[\"").Append(EscapeName(name)).Append("\"]");
            }
            else
            {
                builder.Append('.').Append(name);
            }
        }

        static string EscapeName(string name) =>
            name.Replace("\\", @"\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);
    }

    public static bool operator ==(DeepObserverPath left, DeepObserverPath right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(DeepObserverPath left, DeepObserverPath right)
    {
        return !(left == right);
    }
}

/// <summary>
/// Describes one observed runtime change.
/// </summary>
public readonly record struct DeepObserverChangedEventArgs
{
    /// <summary>
    /// Creates a change event with its typed CLR path and new value.
    /// </summary>
    public DeepObserverChangedEventArgs(DeepObserverPath path, object? value)
    {
        Path = path;
        Value = value;
    }

    /// <summary>
    /// Gets the immutable typed path of the changed member.
    /// </summary>
    public DeepObserverPath Path { get; }

    /// <summary>
    /// Gets the new runtime value, or <see langword="null"/> when the value was cleared.
    /// </summary>
    public object? Value { get; }
}

/// <summary>
/// Receives one runtime change reported by <see cref="DeepObserver"/>.
/// </summary>
/// <param name="e">The changed typed path and runtime value.</param>
public delegate void DeepObserverChangedEventHandler(in DeepObserverChangedEventArgs e);

/// <summary>
/// Excludes a property from DeepObserver traversal and change notifications.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public class DeepObserverIgnoreAttribute : Attribute;

/// <summary>
/// Observes an INotifyPropertyChanged and its properties for changes.
/// Supports nested objects and collections.
/// </summary>
/// <remarks>
/// The observer owns event subscriptions for the complete observed object graph.
/// Call <see cref="Dispose"/> when the owner no longer needs notifications;
/// ordinary .NET events do not provide weak subscriptions automatically.
/// </remarks>
public sealed class DeepObserver(DeepObserverChangedEventHandler handler) : IDisposable
{
    private readonly ConcurrentDictionary<Type, IReadOnlyList<PropertyInfo>> _cachedProperties = [];

    private IReadOnlyList<PropertyInfo> GetPropertyInfos(Type type) =>
        _cachedProperties.GetOrAdd(
            type,
            t => t.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .AsValueEnumerable()
                .Where(p =>
                    p is { CanRead: true, CanWrite: true, IsSpecialName: false } ||
                    p.PropertyType.IsAssignableTo(typeof(INotifyPropertyChanged)))
                .Where(p => p.GetMethod?.GetParameters() is { Length: 0 }) // Ignore
                .Where(p => p.GetCustomAttribute<JsonIgnoreAttribute>() is null)
                .Where(p => p.GetCustomAttribute<DeepObserverIgnoreAttribute>() is null)
                .ToArray());

    private PropertyInfo? GetPropertyInfo(Type type, string propertyName) =>
        GetPropertyInfos(type).AsValueEnumerable().FirstOrDefault(p => p.Name == propertyName);

    private readonly DeepObserverChangedEventHandler _handler = handler;
    private readonly List<Observation> _observations = [];
    private readonly Lock _observationLock = new();
    private bool _isDisposed;

    /// <summary>
    /// Starts observing a root object and returns this observer for fluent setup.
    /// </summary>
    /// <param name="target">The root object to observe.</param>
    /// <param name="basePath">The typed path prefix assigned to the root object.</param>
    public DeepObserver Observe(
        [DynamicallyAccessedMembers(
            DynamicallyAccessedMemberTypes.PublicConstructors |
            DynamicallyAccessedMemberTypes.PublicFields |
            DynamicallyAccessedMemberTypes.PublicProperties)]
        INotifyPropertyChanged target,
        DeepObserverPath basePath = default)
    {
        lock (_observationLock)
        {
            ObjectDisposedException.ThrowIf(_isDisposed, this);
        }

        var observation = new Observation(basePath, target, this);
        lock (_observationLock)
        {
            if (_isDisposed)
            {
                observation.Dispose();
                ObjectDisposedException.ThrowIf(_isDisposed, this);
            }

            _observations.Add(observation);
        }

        return this;
    }

    /// <summary>
    /// Stops all notifications and recursively releases every event subscription.
    /// </summary>
    public void Dispose()
    {
        Observation[] observations;
        lock (_observationLock)
        {
            if (_isDisposed) return;

            _isDisposed = true;
            observations = _observations.ToArray();
            _observations.Clear();
        }

        foreach (var observation in observations) observation.Dispose();
    }

    private sealed class Observation : IDisposable
    {
        private readonly DeepObserverPath _path;
        private readonly Type _targetType;
        private readonly DeepObserver _observer;
        private readonly WeakReference<INotifyPropertyChanged> _targetReference;
        private readonly Dictionary<DeepObserverPathSegment, Observation> _observations = [];
        private readonly Lock _observationLock = new();

        /// <summary>
        /// when <see cref="ObservableCollection{T}"/> is Reset, we cannot get the old items count from event args.
        /// So we need to keep track of the count ourselves.
        /// </summary>
        private int _listItemCount;
        private int _isDisposed;

        public Observation(DeepObserverPath path, INotifyPropertyChanged target, DeepObserver observer)
        {
            _path = path;
            _targetType = target.GetType();
            _observer = observer;
            _targetReference = new WeakReference<INotifyPropertyChanged>(target);

            target.PropertyChanged += HandleTargetPropertyChanged;
            if (target is INotifyCollectionChanged notifyCollectionChanged)
            {
                notifyCollectionChanged.CollectionChanged += HandleTargetCollectionChanged;
            }

            foreach (var propertyInfo in observer.GetPropertyInfos(target.GetType()))
            {
                object? value;
                try
                {
                    value = propertyInfo.GetValue(target);
                }
                catch
                {
                    value = null;
                }

                ObserveObject(DeepObserverPathSegment.Property(propertyInfo.Name), value);
            }

            switch (target)
            {
                case IList list:
                {
                    _listItemCount = list.Count;
                    for (var i = 0; i < list.Count; i++)
                    {
                        ObserveObject(DeepObserverPathSegment.CollectionIndex(i), list[i]);
                    }
                    break;
                }
                case IDictionary dictionary:
                {
                    var enumerator = dictionary.GetEnumerator();
                    using var _ = enumerator as IDisposable;
                    while (enumerator.MoveNext())
                    {
                        var entry = enumerator.Entry;
                        if (entry.Key?.ToString() is not { } key) continue;
                        ObserveObject(DeepObserverPathSegment.DictionaryKey(key), entry.Value);
                    }

                    break;
                }
            }
        }

        /// <summary>
        /// Handles the PropertyChanged event of the observed object.
        /// Evaluates whether the changed property is being observed and updates the observation if necessary.
        /// </summary>
        /// <param name="sender">The object that raised the event.</param>
        /// <param name="e">The event arguments containing the property name.</param>
        private void HandleTargetPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (Volatile.Read(ref _isDisposed) != 0) return;
            if (e.PropertyName is null) return;
            if (!_targetReference.TryGetTarget(out var target)) return;
            if (_observer.GetPropertyInfo(_targetType, e.PropertyName) is not { } propertyInfo) return;

            object? value;
            try
            {
                value = propertyInfo.GetValue(target);
            }
            catch
            {
                value = null;
            }

            if (Volatile.Read(ref _isDisposed) != 0) return;
            var segment = DeepObserverPathSegment.Property(e.PropertyName);
            _observer._handler.Invoke(new DeepObserverChangedEventArgs(_path.Append(segment), value));
            ObserveObject(segment, value);
        }

        /// <summary>
        /// Handles the CollectionChanged event for collections (IList) and dictionaries (IDictionary).
        /// Manages observations for items added, removed, replaced, or moved within the collection.
        /// </summary>
        /// <param name="sender">The collection that raised the event.</param>
        /// <param name="e">The event arguments describing the collection change.</param>
        private void HandleTargetCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (Volatile.Read(ref _isDisposed) != 0) return;

            if (sender is IDictionary dictionary)
            {
                HandleDictionaryCollectionChanged(dictionary, e);
                return;
            }

            if (sender is not IList list) return;

            // Determine the range of indices that need to be updated.
            // Using a range ensures we correctly handle index shifts for Add/Remove/Move operations.
            var startUpdateIndex = -1;
            var endUpdateIndex = -1;
            var notifyCollectionObject = false;

            switch (e.Action)
            {
                case NotifyCollectionChangedAction.Add:
                    // Items added. Indices from NewStartingIndex onwards are shifted.
                    // We must re-observe everything from the insertion point to the end.
                    startUpdateIndex = e.NewStartingIndex;
                    endUpdateIndex = list.Count;
                    break;

                case NotifyCollectionChangedAction.Remove:
                    // Items removed. Indices from OldStartingIndex onwards are shifted down.
                    // We must re-observe from the removal point to the end.
                    startUpdateIndex = e.OldStartingIndex;
                    endUpdateIndex = list.Count;

                    // Notify that the collection object itself has changed (common pattern for removals).
                    notifyCollectionObject = true;
                    break;

                case NotifyCollectionChangedAction.Replace:
                    // Items replaced. Indices do not shift.
                    // Only the range of replaced items needs to be updated.
                    startUpdateIndex = e.NewStartingIndex;
                    endUpdateIndex = e.NewStartingIndex + (e.NewItems?.Count ?? 0);
                    break;

                case NotifyCollectionChangedAction.Move:
                    // Items moved. Indices between OldStartingIndex and NewStartingIndex are affected.
                    // We re-observe the range spanning both the old and new positions.
                    startUpdateIndex = Math.Min(e.OldStartingIndex, e.NewStartingIndex);
                    // The number of items moved is usually e.NewItems.Count (or OldItems.Count).
                    // We extend the range to cover the moved items at their destination.
                    endUpdateIndex = Math.Max(e.OldStartingIndex, e.NewStartingIndex) + (e.NewItems?.Count ?? 0);
                    break;

                case NotifyCollectionChangedAction.Reset:
                    // Collection reset (cleared or drastically changed).
                    // We must re-observe the entire collection.
                    startUpdateIndex = 0;
                    endUpdateIndex = list.Count;
                    notifyCollectionObject = true;
                    break;
            }

            // Cleanup observations for indices that no longer exist (e.g., list shrank).
            // _listItemCount tracks the previous size of the list.
            if (_listItemCount > list.Count)
            {
                for (var i = list.Count; i < _listItemCount; i++)
                {
                    ObserveObject(DeepObserverPathSegment.CollectionIndex(i), null);
                }
            }

            // Update observations for the affected range and notify listeners.
            if (startUpdateIndex != -1)
            {
                for (var i = startUpdateIndex; i < endUpdateIndex; i++)
                {
                    // Ensure we don't go out of bounds if logic above was loose.
                    if (i >= list.Count) break;

                    var val = list[i];

                    // Re-observe: Updates the internal tracking and attaches handlers to the new item.
                    var segment = DeepObserverPathSegment.CollectionIndex(i);
                    ObserveObject(segment, val);

                    // Notify: The value at this index path has changed.
                    _observer._handler.Invoke(new DeepObserverChangedEventArgs(_path.Append(segment), val));
                }
            }

            // If the operation implies a change to the collection object itself (like Remove or Reset),
            // notify the listener about the collection path.
            if (notifyCollectionObject)
            {
                _observer._handler.Invoke(new DeepObserverChangedEventArgs(_path, sender));
            }

            // Update the cached item count for the next event.
            _listItemCount = list.Count;
        }

        /// <summary>
        /// Handles CollectionChanged events specifically for IDictionary targets.
        /// Dictionaries use keys as paths, so index shifting is not a concern.
        /// </summary>
        /// <param name="dictionary">The dictionary that changed.</param>
        /// <param name="e">The event arguments.</param>
        private void HandleDictionaryCollectionChanged(IDictionary dictionary, NotifyCollectionChangedEventArgs e)
        {
            if (e.Action == NotifyCollectionChangedAction.Reset)
            {
                // On Reset, we need to reconcile the current observations with the new state of the dictionary.

                // 1. Identify properties that are part of the object structure (not dictionary entries) to preserve them.
                var properties = _observer.GetPropertyInfos(_targetType).Select(p => p.Name).ToHashSet();

                // 2. Remove observations for keys that are no longer in the dictionary (and aren't properties).
                // We iterate a copy of keys to safely modify the collection.
                DeepObserverPathSegment[] observedSegments;
                lock (_observationLock)
                {
                    observedSegments = _observations.Keys.AsValueEnumerable().ToArray();
                }

                foreach (var segment in observedSegments)
                {
                    if (segment.Kind != DeepObserverPathSegmentKind.Property ||
                        segment.Name is not { } propertyName ||
                        !properties.Contains(propertyName))
                    {
                        // Effectively un-observe by passing null.
                        ObserveObject(segment, null);
                    }
                }

                // 3. Observe all current entries in the dictionary.
                foreach (DictionaryEntry entry in dictionary)
                {
                    if (entry.Key?.ToString() is { } key)
                    {
                        ObserveObject(DeepObserverPathSegment.DictionaryKey(key), entry.Value);
                    }
                }

                // 4. Notify that the dictionary object itself has changed.
                _observer._handler.Invoke(new DeepObserverChangedEventArgs(_path, dictionary));
                return;
            }

            // Handle Removed items
            if (e.OldItems is not null)
            {
                foreach (var item in e.OldItems)
                {
                    if (GetKeyFromItem(item) is { } key)
                    {
                        var segment = DeepObserverPathSegment.DictionaryKey(key.ToString()!);
                        ObserveObject(segment, null); // Stop observing
                        _observer._handler.Invoke(new DeepObserverChangedEventArgs(_path, dictionary)); // Notify removal
                    }
                }
            }

            // Handle Added/New items
            if (e.NewItems is not null)
            {
                foreach (var item in e.NewItems)
                {
                    if (GetKeyFromItem(item) is { } key)
                    {
                        var value = GetValueFromItem(item);
                        var segment = DeepObserverPathSegment.DictionaryKey(key.ToString()!);
                        ObserveObject(segment, value); // Start observing
                        _observer._handler.Invoke(new DeepObserverChangedEventArgs(_path.Append(segment), value)); // Notify addition
                    }
                }
            }
        }

        private static object? GetKeyFromItem(object item)
        {
            switch (item)
            {
                case IKeyValuePair kvp:
                    return kvp.Key;
                case DictionaryEntry de:
                    return de.Key;
            }

            var type = item.GetType();
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(KeyValuePair<,>))
            {
                return type.GetProperty("Key")?.GetValue(item);
            }

            return null;
        }

        private static object? GetValueFromItem(object item)
        {
            switch (item)
            {
                case IKeyValuePair kvp:
                    return kvp.Value;
                case DictionaryEntry de:
                    return de.Value;
            }

            var type = item.GetType();
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(KeyValuePair<,>))
            {
                return type.GetProperty("Value")?.GetValue(item);
            }

            return null;
        }

        private void ObserveObject(DeepObserverPathSegment segment, object? target)
        {
            Observation? removedObservation;
            if (target is not INotifyPropertyChanged notifyPropertyChanged)
            {
                lock (_observationLock)
                {
                    if (Volatile.Read(ref _isDisposed) != 0) return;
                    _observations.Remove(segment, out removedObservation);
                }
            }
            else
            {
                lock (_observationLock)
                {
                    if (Volatile.Read(ref _isDisposed) != 0) return;

                    if (_observations.TryGetValue(segment, out var existingObservation) &&
                        existingObservation._targetReference.TryGetTarget(out var existingTarget) &&
                        Equals(existingTarget, notifyPropertyChanged))
                    {
                        return;
                    }

                    var replacement = new Observation(_path.Append(segment), notifyPropertyChanged, _observer);
                    _observations[segment] = replacement;
                    removedObservation = existingObservation;
                }
            }

            removedObservation?.Dispose();
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _isDisposed, 1) != 0) return;

            Observation[] observations;
            lock (_observationLock)
            {
                observations = _observations.Values.AsValueEnumerable().ToArray();
                _observations.Clear();
            }

            foreach (var observation in observations) observation.Dispose();

            if (_targetReference.TryGetTarget(out var target))
            {
                target.PropertyChanged -= HandleTargetPropertyChanged;
                if (target is INotifyCollectionChanged notifyCollectionChanged)
                {
                    notifyCollectionChanged.CollectionChanged -= HandleTargetCollectionChanged;
                }
            }
        }
    }
}