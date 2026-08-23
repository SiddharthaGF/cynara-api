using System.Text.Json.Nodes;

using Cynara.Application.Modules.FormAi;
using Cynara.Infrastructure.Modules.FormAi;
using Cynara.Infrastructure.Schemas;

using Microsoft.Extensions.AI;

namespace Cynara.Api.Tests;

/// <summary>
/// Backend coverage for the FormAi stream hardening work: the honest safety
/// net (B3), the stream-level first-chunk retry (B5) and the configuration
/// knobs that those features depend on (B1/B4).
/// </summary>
public sealed class FormAiStreamHardeningTests
{
    private const string EmptyClinical = /*lang=json,strict*/ """{"schemaVersion":"1.0.0","fields":[{"id":"any","code":"any.one","type":"text"}]}""";

    private const string EmptyUi = /*lang=json,strict*/ """{"schemaVersion":"1.0.0","clinicalSchemaVersion":"1.0.0","fields":{},"layout":[]}""";

    private const string EmptyRules = /*lang=json,strict*/ """{"schemaVersion":"1.0.0","clinicalSchemaVersion":"1.0.0","fields":{},"validations":[]}""";

    /// <summary>
    /// The UI schema enforces layout items, so a field referencing a missing
    /// clinical id fails validation; clearing only the layout restores a
    /// valid state and exercises the layout-only fallback branch.
    /// </summary>
    [Fact]
    public void TryValidateWithFallback_DropsLayout_ReportsOutcome()
    {
        JsonSchemaValidator validator = CreateValidator();

        JsonObject ui = ParseJsonObject(/*lang=json,strict*/ """
            {
              "schemaVersion": "1.0.0",
              "clinicalSchemaVersion": "1.0.0",
              "fields": {},
              "layout": [
                { "type": "field", "fieldId": "missing-clinical-id" }
              ]
            }
            """);
        JsonObject rules = ParseJsonObject(EmptyRules);

        bool ok = InvokeTryValidateWithFallback(
            validator,
            EmptyClinical,
            ui,
            rules,
            out string finalUi,
            out string finalRules,
            out FormAiFallbackReport report);

        Assert.True(ok);
        Assert.Equal(FormAiFallbackOutcome.DroppedLayout, report.Outcome);
        Assert.Equal(["layout"], report.DroppedLayers);
        Assert.Equal(EmptyRules, finalRules);
        Assert.DoesNotContain("missing-clinical-id", finalUi, StringComparison.Ordinal);
        Assert.Contains("\"layout\":[]", finalUi, StringComparison.Ordinal);
    }

    /// <summary>
    /// A bogus validation op fails the rules schema; clearing the layout alone
    /// cannot fix it, so the fallback keeps stripping down to the rules layer.
    /// </summary>
    [Fact]
    public void TryValidateWithFallback_DropsRules_ReportsOutcome()
    {
        JsonSchemaValidator validator = CreateValidator();
        JsonObject ui = ParseJsonObject(/*lang=json,strict*/ """
            {
              "schemaVersion": "1.0.0",
              "clinicalSchemaVersion": "1.0.0",
              "fields": {},
              "layout": [
                { "type": "field", "fieldId": "missing-clinical-id" }
              ]
            }
            """);

        JsonObject rules = ParseJsonObject(/*lang=json,strict*/ """
            {
              "schemaVersion": "1.0.0",
              "clinicalSchemaVersion": "1.0.0",
              "fields": {},
              "validations": [
                {
                  "code": "BROKEN",
                  "message": "broken",
                  "assert": { "op": "this-op-does-not-exist", "args": [] }
                }
              ]
            }
            """);

        bool ok = InvokeTryValidateWithFallback(
            validator,
            EmptyClinical,
            ui,
            rules,
            out string finalUi,
            out string finalRules,
            out FormAiFallbackReport report);

        Assert.True(ok);
        Assert.Equal(FormAiFallbackOutcome.DroppedValidations, report.Outcome);
        Assert.Equal(["layout", "rules.fields", "rules.validations"], report.DroppedLayers);
        Assert.Equal(EmptyRules, finalRules);
        Assert.DoesNotContain("missing-clinical-id", finalUi, StringComparison.Ordinal);
    }

    [Fact]
    public void FormAiDraftConsistency_DroppedRulesWithClaim_RewritesAssistantMessage()
    {
        const string responseRules = /*lang=json,strict*/ """
            {
              "schemaVersion": "1.0.0",
              "clinicalSchemaVersion": "1.0.0",
              "fields": { "bp-systolic": {} },
              "validations": []
            }
            """;

        FormAiChatResponse result = new(
            Summary: "Updated rules.",
            AssistantMessage: "I added a systolic > diastolic validation.",
            Thinking: null,
            ClinicalSchemaJson: EmptyClinical,
            UiSchemaJson: EmptyUi,
            RulesSchemaJson: responseRules);

        FormAiChatResponse rewritten = FormAiDraftConsistency.EnsureConsistent(
            new FormAiDraftConsistency.EnsureConsistentRequest(
                result,
                EmptyClinical,
                EmptyUi,
                EmptyRules,
                LatestUserContent: "agrega una validación de presión arterial",
                Locale: "es",
                IsRefusal: false,
                Fallback: new FormAiFallbackReport(
                    FormAiFallbackOutcome.DroppedValidations,
                    ["layout", "rules.fields", "rules.validations"])));

        Assert.DoesNotContain("I added", rewritten.AssistantMessage, StringComparison.Ordinal);
        Assert.Contains("descart", rewritten.AssistantMessage, StringComparison.Ordinal);
        Assert.Contains("validations", rewritten.AssistantMessage, StringComparison.Ordinal);
        Assert.Contains("descartado", rewritten.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public void FormAiDraftConsistency_DroppedRulesNoClaim_PreservesMessage()
    {
        const string responseRules = /*lang=json,strict*/ """
            {
              "schemaVersion": "1.0.0",
              "clinicalSchemaVersion": "1.0.0",
              "fields": { "bp-systolic": {} },
              "validations": []
            }
            """;

        FormAiChatResponse result = new(
            Summary: "Updated rules.",
            AssistantMessage: "I tried to add the validation but it failed validation.",
            Thinking: null,
            ClinicalSchemaJson: EmptyClinical,
            UiSchemaJson: EmptyUi,
            RulesSchemaJson: responseRules);

        FormAiChatResponse preserved = FormAiDraftConsistency.EnsureConsistent(
            new FormAiDraftConsistency.EnsureConsistentRequest(
                result,
                EmptyClinical,
                EmptyUi,
                EmptyRules,
                LatestUserContent: "agrega una validación de presión arterial",
                Locale: "es",
                IsRefusal: false,
                Fallback: new FormAiFallbackReport(
                    FormAiFallbackOutcome.DroppedValidations,
                    ["layout", "rules.fields", "rules.validations"])));

        Assert.Equal(result.AssistantMessage, preserved.AssistantMessage);
    }

    [Fact]
    public async Task OpenAiClient_FirstChunkTimeout_RetriesOnce()
    {
        OpenAiConfig config = new(
            ApiKey: "test",
            BaseUrl: "https://api.openai.com/v1",
            Model: "gpt-4o-mini",
            Configured: true,
            JsonObject: true,
            NetworkTimeout: TimeSpan.FromMinutes(1),
            MaxOutputTokens: 4096,
            Temperature: 0.2f,
            TopP: 0.9f,
            FirstChunkTimeout: TimeSpan.FromMilliseconds(50));

        var attempts = 0;
        var streamAttempts = 0;
        StubChatClient first = new(_ =>
        {
            streamAttempts++;
            return new StubAsyncEnumerable(never: true);
        });
        StubChatClient second = new(_ =>
        {
            streamAttempts++;
            return new StubAsyncEnumerable(
                never: false,
                items: [new ChatResponseUpdate(ChatRole.Assistant, "hello")]);
        });

        var index = -1;

        IChatClient ClientFactory()
        {
            index++;
            attempts++;
            return index == 0 ? first : second;
        }

        List<ChatMessage> MessagesFactory()
        {
            return [new ChatMessage(ChatRole.User, "hi")];
        }

        ChatOptions OptionsFactory()
        {
            return new ChatOptions();
        }

        IAsyncEnumerable<OpenAiStreamDelta> stream = OpenAiClient.EnumerateWithFirstChunkRetryAsync(
            ClientFactory,
            MessagesFactory,
            OptionsFactory,
            config,
            CancellationToken.None);

        var emitted = new List<OpenAiStreamDelta>();
        await foreach (OpenAiStreamDelta delta in stream.ConfigureAwait(false))
        {
            emitted.Add(delta);
        }

        Assert.Equal(2, attempts);
        Assert.Equal(2, streamAttempts);
        Assert.Single(emitted);
    }

    [Fact]
    public async Task OpenAiClient_LengthFinishReason_PropagatesTruncation()
    {
        OpenAiConfig config = new(
            ApiKey: "test",
            BaseUrl: "https://api.openai.com/v1",
            Model: "gpt-4o-mini",
            Configured: true,
            JsonObject: true,
            NetworkTimeout: TimeSpan.FromMinutes(1),
            MaxOutputTokens: 4096,
            Temperature: 0.2f,
            TopP: 0.9f,
            FirstChunkTimeout: TimeSpan.Zero);
        var client = new StubChatClient(_ => new StubAsyncEnumerable(
            never: false,
            items:
            [
                new ChatResponseUpdate(ChatRole.Assistant, "partial")
                {
                    FinishReason = ChatFinishReason.Length,
                },
            ]));

        var emitted = new List<OpenAiStreamDelta>();
        await foreach (OpenAiStreamDelta delta in OpenAiClient
                           .EnumerateWithFirstChunkRetryAsync(
                               () => client,
                               () => [new ChatMessage(ChatRole.User, "hi")],
                               () => new ChatOptions(),
                               config,
                               CancellationToken.None)
                           .ConfigureAwait(false))
        {
            emitted.Add(delta);
        }

        Assert.True(emitted.Single().IsTruncated);
    }

    private static JsonObject ParseJsonObject(string json)
    {
        return JsonNode.Parse(json) as JsonObject
            ?? throw new InvalidOperationException("Expected a JSON object.");
    }

    private static JsonSchemaValidator CreateValidator()
    {
        string schemaRoot = Path.GetFullPath(
            Path.Combine(
                AppContext.BaseDirectory,
                "..",
                "..",
                "..",
                "..",
                "..",
                "src",
                "Cynara.Infrastructure",
                "Schemas"));
        if (!Directory.Exists(Path.Combine(schemaRoot, "v1")))
        {
            schemaRoot = Path.Combine(AppContext.BaseDirectory, "Schemas");
        }

        return new JsonSchemaValidator(
            new SchemaFilePaths
            {
                ClinicalSchemaPath = Path.Combine(
                    schemaRoot,
                    "v1",
                    "clinical-schema.schema.json"),
                UiSchemaPath = Path.Combine(
                    schemaRoot,
                    "v1",
                    "ui-schema.schema.json"),
                RulesSchemaPath = Path.Combine(
                    schemaRoot,
                    "v1",
                    "rules-schema.schema.json"),
                WorkflowSchemaPath = Path.Combine(
                    schemaRoot,
                    "v1",
                    "workflow-schema.schema.json"),
            });
    }

    private static bool InvokeTryValidateWithFallback(
        JsonSchemaValidator validator,
        string clinical,
        JsonObject ui,
        JsonObject rules,
        out string finalUi,
        out string finalRules,
        out FormAiFallbackReport report)
    {
        System.Reflection.MethodInfo? method = typeof(FormAiService).GetMethod(
            "TryValidateWithFallback",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        Assert.NotNull(method);

        object?[] args = [validator, clinical, ui, rules, null, null, null];
        bool result = (bool)method!.Invoke(null, args)!;
        finalUi = (string)args[4]!;
        finalRules = (string)args[5]!;
        report = (FormAiFallbackReport)args[6]!;
        return result;
    }

    private sealed class StubChatClient : IChatClient
    {
        private readonly Func<int, IAsyncEnumerable<ChatResponseUpdate>> producer;
        private int calls;

        public StubChatClient(Func<int, IAsyncEnumerable<ChatResponseUpdate>> producer)
        {
            this.producer = producer;
        }

        public void Dispose()
        {
        }

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public object? GetService(Type serviceType, object? serviceKey = null)
        {
            return null;
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            int call = Interlocked.Increment(ref calls);
            return producer(call - 1);
        }
    }

    private sealed class StubAsyncEnumerable : IAsyncEnumerable<ChatResponseUpdate>
    {
        private readonly bool never;
        private readonly ChatResponseUpdate[]? items;

        public StubAsyncEnumerable(bool never, ChatResponseUpdate[]? items = null)
        {
            this.never = never;
            this.items = items;
        }

        public IAsyncEnumerator<ChatResponseUpdate> GetAsyncEnumerator(
            CancellationToken cancellationToken = default)
        {
            return new StubAsyncEnumerator(never, items, cancellationToken);
        }
    }

    /// <summary>Simulates a hanging provider: <c>never</c> blocks until cancelled.</summary>
    private sealed class StubAsyncEnumerator : IAsyncEnumerator<ChatResponseUpdate>
    {
        private readonly bool never;
        private readonly ChatResponseUpdate[]? items;
        private readonly CancellationTokenSource linkedCts;
        private int index = -1;

        public StubAsyncEnumerator(bool never, ChatResponseUpdate[]? items, CancellationToken cancellationToken)
        {
            this.never = never;
            this.items = items;
            linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        }

        public ChatResponseUpdate Current => items![index];

        public async ValueTask DisposeAsync()
        {
            await linkedCts.CancelAsync().ConfigureAwait(false);
            linkedCts.Dispose();
        }

        public async ValueTask<bool> MoveNextAsync()
        {
            if (never)
            {
                try
                {
                    await Task.Delay(Timeout.Infinite, linkedCts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return false;
                }

                return false;
            }

            index++;
            return index < items!.Length;
        }
    }
}
