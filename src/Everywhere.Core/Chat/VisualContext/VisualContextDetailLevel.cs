namespace Everywhere.Chat;

public enum VisualContextDetailLevel
{
    [DynamicResourceKey(LocaleKey.VisualContextDetailLevel_Minimal)]
    Minimal = 0,

    [DynamicResourceKey(LocaleKey.VisualContextDetailLevel_Compact)]
    Compact = 1,

    [DynamicResourceKey(LocaleKey.VisualContextDetailLevel_Detailed)]
    Detailed = 2,
}