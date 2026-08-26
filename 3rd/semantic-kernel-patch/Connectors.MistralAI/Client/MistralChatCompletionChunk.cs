// Copyright (c) Microsoft. All rights reserved.

using System.Collections.Generic;
using System.Text;
using System.Text.Json.Serialization;

namespace Microsoft.SemanticKernel.Connectors.MistralAI.Client;

/// <summary>
/// Represents a chat completion chunk from Mistral.
/// </summary>
internal sealed class MistralChatCompletionChunk
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("object")]
    public string? Object { get; set; }

    [JsonPropertyName("created")]
    public long Created { get; set; }

    [JsonPropertyName("model")]
    public string? Model { get; set; }

    [JsonPropertyName("choices")]
    public List<MistralChatCompletionChoice>? Choices { get; set; }

    [JsonPropertyName("usage")]
    public MistralUsage? Usage { get; set; }

    internal IReadOnlyDictionary<string, object?>? GetMetadata() =>
        this._metadata ??= new Dictionary<string, object?>(5)
        {
            { nameof(MistralChatCompletionChunk.Id), this.Id },
            { nameof(MistralChatCompletionChunk.Model), this.Model },
            { nameof(MistralChatCompletionChunk.Created), this.Created },
            { nameof(MistralChatCompletionChunk.Object), this.Object },
            { nameof(MistralChatCompletionChunk.Usage), this.Usage },
        };

    internal int GetChoiceCount() => this.Choices?.Count ?? 0;

    internal string? GetRole(int index) => this.GetChoice(index)?.Delta?.Role;

    internal string? GetContent(int index) => this.GetChoice(index)?.Delta?.GetTextContent();

    internal string? GetReasoningContent(int index) => this.GetChoice(index)?.Delta?.GetReasoningContent();

    internal int GetChoiceIndex(int index) => this.GetChoice(index)?.Index ?? -1;

    private MistralChatCompletionChoice? GetChoice(int index) =>
        this.Choices is { } choices && (uint)index < (uint)choices.Count ? choices[index] : null;

    internal Encoding? GetEncoding() => null;

    private IReadOnlyDictionary<string, object?>? _metadata;
}
