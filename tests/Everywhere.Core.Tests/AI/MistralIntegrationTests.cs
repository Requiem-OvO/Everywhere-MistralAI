using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using Everywhere.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.MistralAI;

namespace Everywhere.Core.Tests.AI;

public class MistralIntegrationTests
{
    [Test]
    public void CustomAssistant_WhenSerialized_DoesNotIncludeIsMistral()
    {
        var assistant = new CustomAssistant
        {
            Schema = ModelProviderSchema.Mistral
        };

        var json = JsonSerializer.Serialize(assistant);
        using var document = JsonDocument.Parse(json);

        Assert.That(document.RootElement.TryGetProperty(nameof(Assistant.IsMistral), out _), Is.False);
    }

    [Test]
    [NonParallelizable]
    public void GetPromptExecutionSettings_InCommaDecimalCulture_ParsesInvariantSamplingValues()
    {
        var originalCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            var assistant = new CustomAssistant
            {
                Schema = ModelProviderSchema.Mistral,
                ModelId = "mistral-small-latest"
            };
            assistant.MistralOptions.Temperature = "0.25";
            assistant.MistralOptions.TopP = "0.75";
            using var httpClient = new HttpClient();
            var connection = new ModelConnection(
                ModelProviderSchema.Mistral,
                "https://example.com/v1",
                "test-key",
                httpClient,
                null);
            using var mixin = new MistralKernelMixin(assistant, connection, NullLoggerFactory.Instance);

            var settings = (MistralAIPromptExecutionSettings)mixin.GetPromptExecutionSettings();

            Assert.Multiple(() =>
            {
                Assert.That(settings.Temperature, Is.EqualTo(0.25));
                Assert.That(settings.TopP, Is.EqualTo(0.75));
            });
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }

    [Test]
    public async Task GetChatMessageContentsAsync_AfterMaximumToolUse_DoesNotSendToolsOnFinalRequest()
    {
        var handler = new SequentialMistralHandler();
        using var httpClient = new HttpClient(handler);
        var service = new MistralAIChatCompletionService(
            "mistral-small-latest",
            "test-key",
            new Uri("https://example.com/v1"),
            httpClient,
            NullLoggerFactory.Instance,
            skipHttpClientProvider: true);
        var kernel = new Kernel();
        var plugin = kernel.Plugins.AddFromType<WeatherPlugin>();
        var settings = new MistralAIPromptExecutionSettings
        {
            ToolCallBehavior = MistralAIToolCallBehavior.RequiredFunctions(plugin, autoInvoke: true)
        };
        var chatHistory = new ChatHistory
        {
            new ChatMessageContent(AuthorRole.User, "What is the weather?")
        };

        await service.GetChatMessageContentsAsync(chatHistory, settings, kernel);

        Assert.That(handler.RequestBodies, Has.Count.EqualTo(2));
        using var firstRequest = JsonDocument.Parse(handler.RequestBodies[0]);
        using var finalRequest = JsonDocument.Parse(handler.RequestBodies[1]);
        Assert.Multiple(() =>
        {
            Assert.That(firstRequest.RootElement.TryGetProperty("tools", out _), Is.True);
            Assert.That(firstRequest.RootElement.GetProperty("tool_choice").GetString(), Is.EqualTo("any"));
            Assert.That(finalRequest.RootElement.TryGetProperty("tools", out _), Is.False);
            Assert.That(finalRequest.RootElement.TryGetProperty("tool_choice", out _), Is.False);
        });
    }

    private sealed class SequentialMistralHandler : HttpMessageHandler
    {
        public List<string> RequestBodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestBodies.Add(await request.Content!.ReadAsStringAsync(cancellationToken));
            var responseJson = RequestBodies.Count == 1
                ? """
                  {
                    "id": "first",
                    "object": "chat.completion",
                    "created": 1,
                    "model": "mistral-small-latest",
                    "choices": [{
                      "index": 0,
                      "message": {
                        "role": "assistant",
                        "content": "",
                        "tool_calls": [{
                          "id": "call-1",
                          "function": {
                            "name": "WeatherPlugin-GetWeather",
                            "arguments": "{}"
                          }
                        }]
                      },
                      "finish_reason": "tool_calls"
                    }],
                    "usage": { "prompt_tokens": 1, "completion_tokens": 1, "total_tokens": 2 }
                  }
                  """
                : """
                  {
                    "id": "final",
                    "object": "chat.completion",
                    "created": 2,
                    "model": "mistral-small-latest",
                    "choices": [{
                      "index": 0,
                      "message": { "role": "assistant", "content": "Sunny." },
                      "finish_reason": "stop"
                    }],
                    "usage": { "prompt_tokens": 2, "completion_tokens": 1, "total_tokens": 3 }
                  }
                  """;

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson, Encoding.UTF8, "application/json")
            };
        }
    }

    private sealed class WeatherPlugin
    {
        [KernelFunction]
        public static string GetWeather() => "Sunny.";
    }
}