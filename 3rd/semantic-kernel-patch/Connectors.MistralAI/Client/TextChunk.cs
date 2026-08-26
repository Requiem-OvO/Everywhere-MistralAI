// Copyright (c) Microsoft. All rights reserved.

using System.Text.Json.Serialization;

namespace Microsoft.SemanticKernel.Connectors.MistralAI.Client;

internal sealed class TextChunk(string text) : ContentChunk(ContentChunkType.Text)
{
    [JsonPropertyName("text")]
    public string Text { get; } = !string.IsNullOrEmpty(text)
        ? text
        : throw new System.ArgumentException("Text must not be empty.", nameof(text));
}
