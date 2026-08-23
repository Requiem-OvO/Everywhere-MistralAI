using System.Collections.ObjectModel;
using Everywhere.Common;

namespace Everywhere.Chat;

public record ChatContextHistory(
    HumanizedDate Date,
    ObservableCollection<ChatContextMetadata> MetadataList
);