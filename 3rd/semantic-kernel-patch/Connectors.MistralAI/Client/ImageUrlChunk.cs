// Copyright (c) Microsoft. All rights reserved.

using System.Text.Json.Serialization;

namespace Microsoft.SemanticKernel.Connectors.MistralAI.Client;

internal sealed class ImageUrlChunk(string imageUrl) : ContentChunk(ContentChunkType.ImageUrl)
{
    [JsonPropertyName("image_url")]
    public string ImageUrl { get; } = !string.IsNullOrWhiteSpace(imageUrl)
        ? imageUrl
        : throw new System.ArgumentException("Image URL must not be empty.", nameof(imageUrl));
}
