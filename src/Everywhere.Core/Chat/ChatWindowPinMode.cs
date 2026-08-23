namespace Everywhere.Chat;

public enum ChatWindowPinMode
{
    [DynamicResourceKey(LocaleKey.ChatWindowPinMode_RememberPrevious)]
    RememberLast,

    [DynamicResourceKey(LocaleKey.ChatWindowPinMode_AlwaysTopmost)]
    AlwaysTopmost,

    [DynamicResourceKey(LocaleKey.ChatWindowPinMode_AlwaysPinned)]
    AlwaysPinned,

    [DynamicResourceKey(LocaleKey.ChatWindowPinMode_AlwaysUnpinned)]
    AlwaysUnpinned,

    [DynamicResourceKey(LocaleKey.ChatWindowPinMode_TopmostOnInput)]
    TopmostOnInput,

    [DynamicResourceKey(LocaleKey.ChatWindowPinMode_PinOnInput)]
    PinOnInput
}