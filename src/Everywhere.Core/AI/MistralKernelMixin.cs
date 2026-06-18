using System.ClientModel;
using System.ClientModel.Primitives;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using OpenAI;

namespace Everywhere.AI;

/// <summary>
/// A <see cref="KernelMixin"/> for Mistral AI models via Chat Completions.
/// Extends <see cref="OpenAIKernelMixin"/> with provider-specific pipeline policies
/// to handle Mistral's non-standard "thinking" content parts and image_url format.
/// </summary>
public class MistralKernelMixin(
    Assistant assistant,
    ModelConnection connection,
    ILoggerFactory loggerFactory
) : OpenAIKernelMixin(assistant, connection, loggerFactory)
{
    protected override OpenAIClientOptions CreateClientOptions(ModelConnection connection, ILoggerFactory loggerFactory)
    {
        var options = base.CreateClientOptions(connection, loggerFactory);
        options.AddPolicy(new MistralThinkingResponsePolicy(), PipelinePosition.PerCall);
        options.AddPolicy(new MistralRequestPolicy(), PipelinePosition.BeforeTransport);
        return options;
    }
}

/// <summary>
/// Pipeline policy that transforms Mistral's "thinking" content parts into OpenAI's
/// <c>reasoning_content</c> field before the OpenAI SDK parses the response.
/// </summary>
internal sealed class MistralThinkingResponsePolicy : PipelinePolicy
{
    public override async ValueTask ProcessAsync(PipelineMessage message, IReadOnlyList<PipelinePolicy> pipeline, int currentIndex)
    {
        await ProcessNextAsync(message, pipeline, currentIndex).ConfigureAwait(false);

        if (!ShouldTransform(message)) return;

        if (IsStreamingResponse(message.Response!))
        {
            var originalStream = message.Response!.ContentStream;
            if (originalStream is not null)
            {
                message.Response!.ContentStream = new MistralSseTransformStream(originalStream);
            }
        }
        else
        {
            await TransformNonStreamingResponseAsync(message).ConfigureAwait(false);
        }
    }

    public override void Process(PipelineMessage message, IReadOnlyList<PipelinePolicy> pipeline, int currentIndex)
    {
        ProcessNext(message, pipeline, currentIndex);

        if (!ShouldTransform(message)) return;

        if (IsStreamingResponse(message.Response!))
        {
            var originalStream = message.Response!.ContentStream;
            if (originalStream is not null)
            {
                message.Response!.ContentStream = new MistralSseTransformStream(originalStream);
            }
        }
        else
        {
            TransformNonStreamingResponse(message);
        }
    }

    private static bool ShouldTransform(PipelineMessage message)
    {
        var response = message.Response;
        if (response is null || response.Status != 200) return false;

        var requestUri = message.Request?.Uri?.AbsolutePath;
        if (requestUri is null || !requestUri.Contains("chat/completions", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return true;
    }

    private static bool IsStreamingResponse(PipelineResponse response)
    {
        if (!response.Headers.TryGetValue("Content-Type", out var contentType) || contentType is null)
        {
            return false;
        }

        return contentType.Contains("text/event-stream", StringComparison.OrdinalIgnoreCase);
    }

    private static async ValueTask TransformNonStreamingResponseAsync(PipelineMessage message)
    {
        var originalStream = message.Response!.ContentStream;
        if (originalStream is null) return;

        using var memoryStream = new MemoryStream();
        await originalStream.CopyToAsync(memoryStream, message.CancellationToken).ConfigureAwait(false);
        var originalBytes = memoryStream.ToArray();

        var transformedBytes = TransformNonStreamingJson(originalBytes);
        if (transformedBytes is null) return;

        message.Response!.ContentStream = new MemoryStream(transformedBytes, 0, transformedBytes.Length, false, true);
    }

    private static void TransformNonStreamingResponse(PipelineMessage message)
    {
        var originalStream = message.Response!.ContentStream;
        if (originalStream is null) return;

        using var memoryStream = new MemoryStream();
        originalStream.CopyTo(memoryStream);
        var originalBytes = memoryStream.ToArray();

        var transformedBytes = TransformNonStreamingJson(originalBytes);
        if (transformedBytes is null) return;

        message.Response!.ContentStream = new MemoryStream(transformedBytes, 0, transformedBytes.Length, false, true);
    }

    /// <summary>
    /// Transforms Mistral's non-streaming chat completion response by converting
    /// <c>message.content</c> arrays (containing thinking/text chunks) into
    /// <c>message.content</c> (string) and <c>message.reasoning_content</c> (string).
    /// Returns null if no transformation is needed.
    /// </summary>
    private static byte[]? TransformNonStreamingJson(byte[] content)
    {
        try
        {
            var root = JsonNode.Parse(content);
            if (root is not JsonObject rootObj) return null;
            if (rootObj["choices"] is not JsonArray choices) return null;

            var transformed = false;
            foreach (var choice in choices)
            {
                if (choice?["message"] is not JsonObject messageObj) continue;
                if (messageObj["content"] is not JsonArray contentArray) continue;

                ExtractContentParts(contentArray, out var thinkingText, out var answerText);

                // Replace content array with string (empty if no text element found)
                messageObj["content"] = answerText ?? "";
                if (thinkingText is not null)
                {
                    messageObj["reasoning_content"] = thinkingText;
                }

                transformed = true;
            }

            if (!transformed) return null;
            return JsonSerializer.SerializeToUtf8Bytes(root);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Extracts thinking and answer text from Mistral content parts array.
    /// </summary>
    private static void ExtractContentParts(JsonArray contentArray, out string? thinkingText, out string? answerText)
    {
        thinkingText = null;
        answerText = null;

        foreach (var part in contentArray)
        {
            if (part is not JsonObject partObj) continue;
            var type = partObj["type"]?.GetValue<string>();
            if (type == "thinking")
            {
                if (partObj["thinking"] is JsonArray thinkingArray)
                {
                    foreach (var thinkingPart in thinkingArray)
                    {
                        if (thinkingPart?["text"]?.GetValue<string>() is { } t)
                        {
                            thinkingText = (thinkingText ?? "") + t;
                        }
                    }
                }
            }
            else if (type == "text")
            {
                if (partObj["text"]?.GetValue<string>() is { } t)
                {
                    answerText = (answerText ?? "") + t;
                }
            }
        }
    }
}

/// <summary>
/// Pipeline policy that transforms the request body for Mistral compatibility:
/// 1. Removes the <c>detail</c> field from <c>image_url</c> content parts (Mistral rejects this field).
/// 2. Converts OpenAI's <c>reasoning_content</c> field on assistant messages back into Mistral's
///    native <c>ThinkChunk</c> format within the <c>content</c> array, as required for multi-turn
///    conversations with reasoning. See https://docs.mistral.ai/capabilities/reasoning.
/// </summary>
internal sealed class MistralRequestPolicy : PipelinePolicy
{
    public override async ValueTask ProcessAsync(PipelineMessage message, IReadOnlyList<PipelinePolicy> pipeline, int currentIndex)
    {
        await TransformRequestAsync(message).ConfigureAwait(false);
        await ProcessNextAsync(message, pipeline, currentIndex).ConfigureAwait(false);
    }

    public override void Process(PipelineMessage message, IReadOnlyList<PipelinePolicy> pipeline, int currentIndex)
    {
        TransformRequest(message);
        ProcessNext(message, pipeline, currentIndex);
    }

    private static async ValueTask TransformRequestAsync(PipelineMessage message)
    {
        var content = message.Request?.Content;
        if (content is null) return;

        using var memoryStream = new MemoryStream();
        await content.WriteToAsync(memoryStream, message.CancellationToken).ConfigureAwait(false);
        var originalBytes = memoryStream.ToArray();

        var transformedBytes = TransformRequestBody(originalBytes);
        if (transformedBytes is null) return;

        message.Request!.Content = BinaryContent.Create(BinaryData.FromBytes(transformedBytes));
        message.Request!.Headers.Remove("Content-Length");
    }

    private static void TransformRequest(PipelineMessage message)
    {
        var content = message.Request?.Content;
        if (content is null) return;

        using var memoryStream = new MemoryStream();
        content.WriteTo(memoryStream, message.CancellationToken);
        var originalBytes = memoryStream.ToArray();

        var transformedBytes = TransformRequestBody(originalBytes);
        if (transformedBytes is null) return;

        message.Request!.Content = BinaryContent.Create(BinaryData.FromBytes(transformedBytes));
        message.Request!.Headers.Remove("Content-Length");
    }

    /// <summary>
    /// Transforms the request body for Mistral compatibility:
    /// 1. Converts <c>reasoning_content</c> on assistant messages back to Mistral's <c>ThinkChunk</c> format.
    /// 2. Removes the <c>detail</c> field from <c>image_url</c> content parts.
    /// Returns null if no transformation is needed.
    /// </summary>
    private static byte[]? TransformRequestBody(byte[] bytes)
    {
        try
        {
            var root = JsonNode.Parse(bytes);
            if (root is not JsonObject rootObj) return null;
            if (rootObj["messages"] is not JsonArray messages) return null;

            var transformed = false;
            foreach (var message in messages)
            {
                if (message is not JsonObject messageObj) continue;

                // Convert reasoning_content back to Mistral's ThinkChunk format for assistant messages.
                // The OpenAIKernelMixin injects reasoning_content via JsonPatch on every assistant message
                // (even empty string), but Mistral rejects this field in requests with HTTP 422.
                if (messageObj["role"]?.GetValue<string>() == "assistant" &&
                    messageObj["reasoning_content"] is JsonValue reasoningNode)
                {
                    var reasoningText = reasoningNode.GetValue<string>();
                    messageObj.Remove("reasoning_content");

                    if (!string.IsNullOrEmpty(reasoningText))
                    {
                        // Build Mistral's native content array: ThinkChunk + TextChunk
                        var contentArray = new JsonArray
                        {
                            new JsonObject
                            {
                                ["type"] = "thinking",
                                ["thinking"] = new JsonArray
                                {
                                    new JsonObject
                                    {
                                        ["type"] = "text",
                                        ["text"] = reasoningText
                                    }
                                }
                            }
                        };

                        // Append TextChunk only if there is non-empty content text
                        var contentText = messageObj["content"]?.AsValue().GetValue<string>();
                        if (!string.IsNullOrEmpty(contentText))
                        {
                            contentArray.Add(new JsonObject
                            {
                                ["type"] = "text",
                                ["text"] = contentText
                            });
                        }

                        messageObj["content"] = contentArray;
                    }

                    transformed = true;
                }

                // Remove detail field from image_url content parts
                if (messageObj["content"] is JsonArray contentArrayExisting)
                {
                    foreach (var part in contentArrayExisting)
                    {
                        if (part is not JsonObject partObj) continue;
                        if (partObj["type"]?.GetValue<string>() != "image_url") continue;
                        if (partObj["image_url"] is JsonObject imageUrl && imageUrl.ContainsKey("detail"))
                        {
                            imageUrl.Remove("detail");
                            transformed = true;
                        }
                    }
                }
            }

            if (!transformed) return null;
            return JsonSerializer.SerializeToUtf8Bytes(root);
        }
        catch
        {
            return null;
        }
    }
}

/// <summary>
/// A transforming stream wrapper that reads SSE events from the inner stream,
/// transforms Mistral's "thinking" content parts in each <c>data:</c> event,
/// and outputs the transformed SSE event. Does not buffer the entire stream.
/// </summary>
internal sealed class MistralSseTransformStream : Stream
{
    private readonly Stream _innerStream;
    private readonly StreamReader _reader;
    private readonly MemoryStream _outputBuffer;
    private bool _done;

    public MistralSseTransformStream(Stream innerStream)
    {
        _innerStream = innerStream;
        _reader = new StreamReader(innerStream, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, bufferSize: 1024, leaveOpen: true);
        _outputBuffer = new MemoryStream();
    }

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        // Fill the output buffer if it's empty
        while (_outputBuffer.Position >= _outputBuffer.Length && !_done)
        {
            _outputBuffer.SetLength(0);
            _outputBuffer.Position = 0;

            var line = _reader.ReadLine();
            if (line is null)
            {
                _done = true;
                break;
            }

            ProcessLine(line);
            _outputBuffer.Position = 0;
        }

        if (_outputBuffer.Position >= _outputBuffer.Length) return 0;
        return _outputBuffer.Read(buffer, offset, count);
    }

    public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        while (_outputBuffer.Position >= _outputBuffer.Length && !_done)
        {
            _outputBuffer.SetLength(0);
            _outputBuffer.Position = 0;

            var line = await _reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (line is null)
            {
                _done = true;
                break;
            }

            ProcessLine(line);
            _outputBuffer.Position = 0;
        }

        if (_outputBuffer.Position >= _outputBuffer.Length) return 0;
        return await _outputBuffer.ReadAsync(buffer, offset, count, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Processes a single SSE line (without the trailing newline) and writes the
    /// transformed output (with newline) to the output buffer.
    /// </summary>
    private void ProcessLine(string line)
    {
        if (line.StartsWith("data: ", StringComparison.Ordinal))
        {
            var json = line.Substring("data: ".Length);

            // Pass through [DONE] marker unchanged
            if (json == "[DONE]")
            {
                WriteOutput("data: [DONE]\n");
                return;
            }

            var transformed = TransformSseJson(json);
            if (transformed is not null)
            {
                WriteOutput("data: " + transformed + "\n");
                return;
            }
        }

        // Pass through unchanged (re-add the newline that ReadLine stripped)
        WriteOutput(line + "\n");
    }

    private void WriteOutput(string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        _outputBuffer.Write(bytes, 0, bytes.Length);
    }

    /// <summary>
    /// Transforms a single SSE chunk's JSON by converting <c>delta.content</c> arrays
    /// (containing thinking/text chunks) into <c>delta.content</c> (string) and/or
    /// <c>delta.reasoning_content</c> (string). Returns null if no transformation is needed.
    /// </summary>
    private static string? TransformSseJson(string json)
    {
        try
        {
            var root = JsonNode.Parse(json);
            if (root is not JsonObject rootObj) return null;
            if (rootObj["choices"] is not JsonArray choices) return null;

            var transformed = false;
            foreach (var choice in choices)
            {
                if (choice?["delta"] is not JsonObject delta) continue;
                if (delta["content"] is not JsonArray contentArray) continue;

                ExtractContentParts(contentArray, out var thinkingText, out var answerText);

                // Remove the content array
                delta.Remove("content");

                // Set content to the text string (only if there's text)
                if (answerText is not null)
                {
                    delta["content"] = answerText;
                }

                // Set reasoning_content if there's thinking text
                if (thinkingText is not null)
                {
                    delta["reasoning_content"] = thinkingText;
                }

                transformed = true;
            }

            if (!transformed) return null;
            return root.ToJsonString();
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Extracts thinking and answer text from Mistral content parts array.
    /// </summary>
    private static void ExtractContentParts(JsonArray contentArray, out string? thinkingText, out string? answerText)
    {
        thinkingText = null;
        answerText = null;

        foreach (var part in contentArray)
        {
            if (part is not JsonObject partObj) continue;
            var type = partObj["type"]?.GetValue<string>();
            if (type == "thinking")
            {
                if (partObj["thinking"] is JsonArray thinkingArray)
                {
                    foreach (var thinkingPart in thinkingArray)
                    {
                        if (thinkingPart?["text"]?.GetValue<string>() is { } t)
                        {
                            thinkingText = (thinkingText ?? "") + t;
                        }
                    }
                }
            }
            else if (type == "text")
            {
                if (partObj["text"]?.GetValue<string>() is { } t)
                {
                    answerText = (answerText ?? "") + t;
                }
            }
        }
    }

    public override void Flush() { }

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _reader.Dispose();
            _outputBuffer.Dispose();
            _innerStream.Dispose();
        }

        base.Dispose(disposing);
    }
}
