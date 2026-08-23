namespace Everywhere.Views;

public interface IMainViewNavigationSubItem : IMainViewNavigationItem
{
    Type GroupType { get; }

    IDynamicResourceKey? DescriptionKey { get; }
}