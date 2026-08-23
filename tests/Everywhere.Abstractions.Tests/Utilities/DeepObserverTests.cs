using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Everywhere.Collections;
using Everywhere.Utilities;

namespace Everywhere.Abstractions.Tests.Utilities;

[TestFixture]
public partial class DeepObserverTests
{
    private partial class TestViewModel : ObservableObject
    {
        [ObservableProperty]
        public partial string? Name { get; set; }

        [ObservableProperty]
        // ReSharper disable once UnusedMember.Local
        public partial int Value { get; set; }

        [ObservableProperty]
        public partial TestViewModel? Child { get; set; }

        [ObservableProperty]
        public partial ObservableCollection<TestViewModel>? Items { get; set; }

        [ObservableProperty]
        public partial ObservableDictionary<string, string>? Dict { get; set; }

        [ObservableProperty]
        public partial ObservableDictionary<string, TestViewModel>? ObjectDict { get; set; }
    }

    private static void AssertPath(DeepObserverPath actual, params DeepObserverPathSegment[] expected) =>
        Assert.That(actual.AsSpan().ToArray(), Is.EqualTo(expected));

    private static bool PathEquals(DeepObserverPath actual, params DeepObserverPathSegment[] expected) =>
        actual.AsSpan().SequenceEqual(expected);

    [Test]
    public void Observe_ShouldTrigger_OnSimplePropertyChange()
    {
        var vm = new TestViewModel { Name = "Initial" };
        var changes = new List<DeepObserverChangedEventArgs>();
        using var observer = new DeepObserver((in e) => changes.Add(e));
        
        observer.Observe(vm);
        
        vm.Name = "Changed";

        using var _ = Assert.EnterMultipleScope();
        Assert.That(changes, Has.Count.EqualTo(1));
        AssertPath(changes[0].Path, DeepObserverPathSegment.Property(nameof(TestViewModel.Name)));
        Assert.That(changes[0].Value, Is.EqualTo("Changed"));
    }

    [Test]
    public void Observe_ShouldTrigger_OnNestedPropertyChange()
    {
        var child = new TestViewModel { Name = "Child" };
        var vm = new TestViewModel { Child = child };
        var changes = new List<DeepObserverChangedEventArgs>();
        using var observer = new DeepObserver((in e) => changes.Add(e));
        
        observer.Observe(vm);
        
        child.Name = "ChildChanged";

        using var _ = Assert.EnterMultipleScope();
        Assert.That(changes, Has.Count.EqualTo(1));
        AssertPath(
            changes[0].Path,
            DeepObserverPathSegment.Property(nameof(TestViewModel.Child)),
            DeepObserverPathSegment.Property(nameof(TestViewModel.Name)));
        Assert.That(changes[0].Value, Is.EqualTo("ChildChanged"));
    }

    [Test]
    public void Observe_ShouldTrigger_OnCollectionAdd()
    {
        var vm = new TestViewModel { Items = [] };
        var changes = new List<DeepObserverChangedEventArgs>();
        using var observer = new DeepObserver((in e) => changes.Add(e));
        
        observer.Observe(vm);
        
        var newItem = new TestViewModel { Name = "NewItem" };
        vm.Items.Add(newItem);
        
        // Add triggers change for the index
        using var _ = Assert.EnterMultipleScope();
        Assert.That(changes, Has.Count.EqualTo(1));
        AssertPath(
            changes[0].Path,
            DeepObserverPathSegment.Property(nameof(TestViewModel.Items)),
            DeepObserverPathSegment.CollectionIndex(0));
        Assert.That(changes[0].Value, Is.EqualTo(newItem));
    }

    [Test]
    public void Observe_ShouldTrigger_OnCollectionRemove()
    {
        var item1 = new TestViewModel { Name = "Item1" };
        var vm = new TestViewModel { Items = [item1] };
        var changes = new List<DeepObserverChangedEventArgs>();
        using var observer = new DeepObserver((in e) => changes.Add(e));
        
        observer.Observe(vm);
        
        vm.Items.Remove(item1);
        
        // Remove triggers notification for the whole collection object property
        using var _ = Assert.EnterMultipleScope();
        Assert.That(changes, Has.Count.EqualTo(1));
        AssertPath(changes[0].Path, DeepObserverPathSegment.Property(nameof(TestViewModel.Items)));
        Assert.That(changes[0].Value, Is.EqualTo(vm.Items));
    }

    [Test]
    public void Observe_ShouldTrigger_OnNestedCollectionItemChange()
    {
        var item1 = new TestViewModel { Name = "Item1" };
        var vm = new TestViewModel { Items = [item1] };
        var changes = new List<DeepObserverChangedEventArgs>();
        using var observer = new DeepObserver((in e) => changes.Add(e));
        
        observer.Observe(vm);
        
        item1.Name = "Item1Changed";

        using var _ = Assert.EnterMultipleScope();
        Assert.That(changes, Has.Count.EqualTo(1));
        AssertPath(
            changes[0].Path,
            DeepObserverPathSegment.Property(nameof(TestViewModel.Items)),
            DeepObserverPathSegment.CollectionIndex(0),
            DeepObserverPathSegment.Property(nameof(TestViewModel.Name)));
        Assert.That(changes[0].Value, Is.EqualTo("Item1Changed"));
    }

    [Test]
    public void Observe_ShouldPreserve_ColonInDictionaryKey()
    {
        var child = new TestViewModel { Name = "Initial" };
        var vm = new TestViewModel
        {
            ObjectDict = new ObservableDictionary<string, TestViewModel> { ["provider:key"] = child }
        };
        var changes = new List<DeepObserverChangedEventArgs>();
        using var observer = new DeepObserver((in e) => changes.Add(e));

        observer.Observe(vm);

        child.Name = "Changed";

        Assert.That(changes, Has.Count.EqualTo(1));
        AssertPath(
            changes[0].Path,
            DeepObserverPathSegment.Property(nameof(TestViewModel.ObjectDict)),
            DeepObserverPathSegment.DictionaryKey("provider:key"),
            DeepObserverPathSegment.Property(nameof(TestViewModel.Name)));
        Assert.That(changes[0].Path.ToString(), Is.EqualTo("$.ObjectDict[\"provider:key\"].Name"));
    }

    [Test]
    public void Observe_ShouldHandle_CollectionReset()
    {
        var item1 = new TestViewModel { Name = "Item1" };
        var vm = new TestViewModel { Items = [item1] };
        var changes = new List<DeepObserverChangedEventArgs>();
        using var observer = new DeepObserver((in e) => changes.Add(e));
        
        observer.Observe(vm);
        
        vm.Items.Clear(); // Triggers Reset

        using var _ = Assert.EnterMultipleScope();
        Assert.That(changes, Has.Count.EqualTo(1));
        AssertPath(changes[0].Path, DeepObserverPathSegment.Property(nameof(TestViewModel.Items)));
        Assert.That(changes[0].Value, Is.EqualTo(vm.Items));
    }

    [Test]
    public void Observe_ShouldTrigger_OnDictionaryAdd()
    {
        var vm = new TestViewModel { Dict = new ObservableDictionary<string, string>() };
        var changes = new List<DeepObserverChangedEventArgs>();
        using var observer = new DeepObserver((in e) => changes.Add(e));
        
        observer.Observe(vm);
        
        vm.Dict.Add("Key1", "Value1");

        using var _ = Assert.EnterMultipleScope();
        Assert.That(changes, Has.Count.EqualTo(1));
        AssertPath(
            changes[0].Path,
            DeepObserverPathSegment.Property(nameof(TestViewModel.Dict)),
            DeepObserverPathSegment.DictionaryKey("Key1"));
        Assert.That(changes[0].Value, Is.EqualTo("Value1"));
    }
    
    [Test]
    public void Observe_ShouldTrigger_OnDictionaryRemove()
    {
        var vm = new TestViewModel { Dict = new ObservableDictionary<string, string> { { "Key1", "Value1" } } };
        var changes = new List<DeepObserverChangedEventArgs>();
        using var observer = new DeepObserver((in e) => changes.Add(e));
        
        observer.Observe(vm);
        
        vm.Dict.Remove("Key1");

        using var _ = Assert.EnterMultipleScope();
        Assert.That(changes, Has.Count.EqualTo(1));
        AssertPath(changes[0].Path, DeepObserverPathSegment.Property(nameof(TestViewModel.Dict)));
        Assert.That(changes[0].Value, Is.SameAs(vm.Dict));
        Assert.That(vm.Dict, Is.Empty);
    }
    
    [Test]
    public void Observe_ShouldTrigger_OnDictionaryReset()
    {
        var vm = new TestViewModel { Dict = new ObservableDictionary<string, string> { { "Key1", "Value1" } } };
        var changes = new List<DeepObserverChangedEventArgs>();
        using var observer = new DeepObserver((in e) => changes.Add(e));
        
        observer.Observe(vm);
        
        vm.Dict.Clear(); // Triggers Reset

        using var _ = Assert.EnterMultipleScope();
        Assert.That(changes, Has.Count.EqualTo(1));
        AssertPath(changes[0].Path, DeepObserverPathSegment.Property(nameof(TestViewModel.Dict)));
        Assert.That(changes[0].Value, Is.EqualTo(vm.Dict));
    }

    [Test]
    public void Observe_ShouldStopObserving_AfterDispose()
    {
        var vm = new TestViewModel { Name = "Initial" };
        var changes = new List<DeepObserverChangedEventArgs>();
        var observer = new DeepObserver((in e) => changes.Add(e));
        
        observer.Observe(vm);
        observer.Dispose();
        
        vm.Name = "Changed";
        
        Assert.That(changes, Is.Empty);
    }

    [Test]
    public void Observe_ShouldStopObserving_NestedObjectsAfterDispose()
    {
        var child = new TestViewModel { Name = "Initial" };
        var vm = new TestViewModel { Child = child };
        var changes = new List<DeepObserverChangedEventArgs>();
        var observer = new DeepObserver((in e) => changes.Add(e));

        observer.Observe(vm);
        observer.Dispose();

        child.Name = "Changed";

        Assert.That(changes, Is.Empty);
    }

    [Test]
    public void Observe_ShouldReject_NewRootAfterDispose()
    {
        var observer = new DeepObserver((in _) => { });
        observer.Dispose();

        Assert.Throws<ObjectDisposedException>(() => observer.Observe(new TestViewModel()));
    }

    [Test]
    public void Observe_ShouldUpdateObservation_WhenNestedObjectReplaced()
    {
        var grandchild = new TestViewModel { Name = "Grandchild" };
        var child1 = new TestViewModel { Child = grandchild, Name = "Child1" };
        var child2 = new TestViewModel { Name = "Child2" };
        var vm = new TestViewModel { Child = child1 };
        var changes = new List<DeepObserverChangedEventArgs>();
        using var observer = new DeepObserver((in e) => changes.Add(e));
        
        observer.Observe(vm);
        
        // Change Child to child2
        vm.Child = child2;
        
        // Verify child1 is no longer observed
        changes.Clear();
        child1.Name = "Child1Changed";
        Assert.That(changes, Is.Empty);

        // Disposing the replaced child must also dispose its descendants.
        grandchild.Name = "GrandchildChanged";
        Assert.That(changes, Is.Empty);

        // Verify child2 is observed
        child2.Name = "Child2Changed";
        Assert.That(changes, Has.Count.EqualTo(1));
        AssertPath(
            changes[0].Path,
            DeepObserverPathSegment.Property(nameof(TestViewModel.Child)),
            DeepObserverPathSegment.Property(nameof(TestViewModel.Name)));
    }

    [Test]
    public void Observe_ShouldTrigger_OnCollectionReplace()
    {
        var item1 = new TestViewModel { Name = "Item1" };
        var item2 = new TestViewModel { Name = "Item2" };
        var vm = new TestViewModel { Items = [item1] };
        var changes = new List<DeepObserverChangedEventArgs>();
        using var observer = new DeepObserver((in e) => changes.Add(e));
        
        observer.Observe(vm);
        
        vm.Items[0] = item2;

        using var _ = Assert.EnterMultipleScope();
        Assert.That(changes, Has.Count.EqualTo(1));
        AssertPath(
            changes[0].Path,
            DeepObserverPathSegment.Property(nameof(TestViewModel.Items)),
            DeepObserverPathSegment.CollectionIndex(0));
        Assert.That(changes[0].Value, Is.EqualTo(item2));
    }

    [Test]
    public void Observe_ShouldTrigger_OnCollectionMove()
    {
        var item1 = new TestViewModel { Name = "Item1" };
        var item2 = new TestViewModel { Name = "Item2" };
        var item3 = new TestViewModel { Name = "Item3" };
        var vm = new TestViewModel { Items = [item1, item2, item3] };
        var changes = new List<DeepObserverChangedEventArgs>();
        using var observer = new DeepObserver((in e) => changes.Add(e));
        
        observer.Observe(vm);
        
        // Move Item1 from index 0 to index 2
        // Initial: [Item1, Item2, Item3]
        // Final:   [Item2, Item3, Item1]
        // Changes expected at 0, 1, 2
        vm.Items.Move(0, 2);
        
        // Assert.That(changes, Has.Count.GreaterThanOrEqualTo(3)); 
        // 0 -> Item2
        // 1 -> Item3
        // 2 -> Item1
        
        var change0 = changes.LastOrDefault(c => PathEquals(
            c.Path,
            DeepObserverPathSegment.Property(nameof(TestViewModel.Items)),
            DeepObserverPathSegment.CollectionIndex(0)));
        var change1 = changes.LastOrDefault(c => PathEquals(
            c.Path,
            DeepObserverPathSegment.Property(nameof(TestViewModel.Items)),
            DeepObserverPathSegment.CollectionIndex(1)));
        var change2 = changes.LastOrDefault(c => PathEquals(
            c.Path,
            DeepObserverPathSegment.Property(nameof(TestViewModel.Items)),
            DeepObserverPathSegment.CollectionIndex(2)));

        using var _ = Assert.EnterMultipleScope();
        Assert.That(change0, Is.Not.Default);
        Assert.That(change0.Value, Is.EqualTo(item2));
        
        Assert.That(change1, Is.Not.Default);
        Assert.That(change1.Value, Is.EqualTo(item3));
        
        Assert.That(change2, Is.Not.Default);
        Assert.That(change2.Value, Is.EqualTo(item1));
    }
}