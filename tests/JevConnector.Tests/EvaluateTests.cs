using System.Net;
using Newtonsoft.Json.Linq;
using Xunit;

namespace JevConnector.Tests;

public class EvaluateTests
{
    private static HttpResponseMessage Ok(HttpRequestMessage _) => Harness.Json(HttpStatusCode.OK, Harness.Fixture("systemone-response.json"));

    [Fact]
    public async Task Builder_request_is_forwarded_once_as_native_payload()
    {
        var (response, context) = await Harness.RunAsync("Evaluate", HttpMethod.Post, "/systemone", Harness.Fixture("builder-request.json"), Ok);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var sent = Assert.Single(context.Sent);
        Assert.Equal(HttpMethod.Post, sent.Method);
        Assert.Equal("https://api.typesafe.ai/v1/systemone", sent.Uri.ToString());
        Assert.Equal("Bearer " + Harness.ApiKey, sent.Authorization);
        Assert.True(JToken.DeepEquals(JObject.Parse(Harness.Fixture("builder-expected-payload.json")), JObject.Parse(sent.Body)), sent.Body);
    }

    [Fact]
    public async Task Successful_response_is_returned_unchanged()
    {
        var upstream = Ok(null);
        var (response, _) = await Harness.RunAsync("Evaluate", HttpMethod.Post, "/systemone", Harness.Fixture("builder-request.json"), _ => upstream);

        Assert.Same(upstream, response);
    }

    [Theory]
    [InlineData("Bearer " + Harness.ApiKey)]
    [InlineData("bearer " + Harness.ApiKey)]
    [InlineData("  " + Harness.ApiKey + "  ")]
    public async Task Authorization_is_sent_as_single_bearer_token(string configured)
    {
        var (_, context) = await Harness.RunAsync("Evaluate", HttpMethod.Post, "/systemone", Harness.Fixture("builder-request.json"), Ok, configured);

        Assert.Equal("Bearer " + Harness.ApiKey, Assert.Single(context.Sent).Authorization);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Bearer ")]
    public async Task Missing_api_key_is_rejected_without_calling_typesafe(string configured)
    {
        var (response, context) = await Harness.RunAsync("Evaluate", HttpMethod.Post, "/systemone", Harness.Fixture("builder-request.json"), Ok, configured);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("missing_api_key", (string)(await ReadJson(response))["error"]["code"]);
        Assert.Empty(context.Sent);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "{\"detail\":\"Invalid API key\"}")]
    [InlineData(HttpStatusCode.UnprocessableEntity, "{\"detail\":[{\"loc\":[\"body\",\"questions\"],\"msg\":\"bad\"}]}")]
    [InlineData((HttpStatusCode)429, "{\"detail\":\"Rate limited\"}")]
    [InlineData((HttpStatusCode)529, "{\"detail\":\"Overloaded\"}")]
    public async Task Upstream_errors_pass_through_without_retry(HttpStatusCode status, string body)
    {
        var upstream = Harness.Json(status, body);
        upstream.Headers.TryAddWithoutValidation("retry-after", "3");

        foreach (var operation in new[] { "Evaluate", "EvaluateJson" })
        {
            var (response, context) = await Harness.RunAsync(operation, HttpMethod.Post, "/systemone", Harness.Fixture("builder-request.json"), _ => upstream);

            Assert.Same(upstream, response);
            Assert.Single(context.Sent);
            Assert.Equal("3", response.Headers.GetValues("retry-after").Single());
        }
    }

    [Fact]
    public async Task EvaluateJson_wraps_native_response_as_text()
    {
        var native = Harness.Fixture("systemone-response.json");
        var (response, context) = await Harness.RunAsync("EvaluateJson", HttpMethod.Post, "/_connector/evaluate-json", Harness.Fixture("builder-request.json"), _ =>
        {
            var upstream = Harness.Json(HttpStatusCode.OK, native);
            upstream.Headers.TryAddWithoutValidation("x-typesafe-request-id", "req-1");
            return upstream;
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("https://api.typesafe.ai/v1/systemone", Assert.Single(context.Sent).Uri.ToString());
        var json = await ReadJson(response);
        Assert.Equal(native, (string)json["responseJson"]);
        Assert.Equal("req-1", response.Headers.GetValues("x-typesafe-request-id").Single());
    }

    [Fact]
    public async Task Json_mode_preserves_structured_native_questions()
    {
        var questions = new JObject
        {
            ["policy check"] = new JObject
            {
                ["type"] = "choice",
                ["instructions"] = new JObject { ["task"] = "Classify", ["rules"] = new JArray("a", "b") },
                ["criteria"] = new JObject { ["allow"] = new JObject { ["when"] = "safe" }, ["deny"] = null, ["review"] = new JArray("x") },
                ["extra"] = 42
            },
            ["ok"] = new JObject { ["type"] = "noul" },
            ["rubric"] = new JObject { ["type"] = "score", ["instructions"] = null, ["criteria"] = new JArray(null, new JObject { ["level"] = 1 }) }
        };
        var request = new JObject
        {
            ["state"] = "{\"order\":{\"id\":7,\"total\":12.50}}",
            ["stateFormat"] = "JSON",
            ["definition"] = new JObject { ["mode"] = "JSON", ["questionsJson"] = questions.ToString() }
        };

        var (_, context) = await Harness.RunAsync("Evaluate", HttpMethod.Post, "/systemone", request.ToString(), Ok);

        var body = Assert.Single(context.Sent).Body;
        var payload = JObject.Parse(body);
        Assert.True(JToken.DeepEquals(questions, payload["questions"]), payload.ToString());
        Assert.Equal(JTokenType.Object, payload["state"].Type);
        Assert.Contains("\"total\":12.50", body);
        Assert.Equal("jev-latest", (string)payload["model"]);
    }

    [Fact]
    public async Task Text_state_is_sent_as_string_even_if_it_looks_like_json()
    {
        var body = JObject.Parse(Harness.Fixture("builder-request.json"));
        body["state"] = "{\"a\":1}";
        body.Remove("stateFormat");

        var (_, context) = await Harness.RunAsync("Evaluate", HttpMethod.Post, "/systemone", body.ToString(), Ok);

        Assert.Equal(JTokenType.String, JObject.Parse(Assert.Single(context.Sent).Body)["state"].Type);
    }

    [Fact]
    public async Task Direct_callers_can_send_object_state_and_native_questions_object()
    {
        var body = new JObject
        {
            ["state"] = new JArray(new JObject { ["role"] = "user", ["text"] = "hi" }),
            ["model"] = "  ",
            ["definition"] = new JObject { ["questionsJson"] = new JObject { ["greeting"] = new JObject { ["type"] = "noul", ["instructions"] = "Is it a greeting?" } } }
        };

        var (_, context) = await Harness.RunAsync("Evaluate", HttpMethod.Post, "/systemone", body.ToString(), Ok);

        var payload = JObject.Parse(Assert.Single(context.Sent).Body);
        Assert.Equal(JTokenType.Array, payload["state"].Type);
        Assert.Equal("jev-latest", (string)payload["model"]);
        Assert.Equal("noul", (string)payload["questions"]["greeting"]["type"]);
    }

    [Theory]
    [MemberData(nameof(InvalidRequests))]
    public async Task Invalid_requests_are_rejected_locally(string body, string expectedCode, string expectedPath)
    {
        var (response, context) = await Harness.RunAsync("Evaluate", HttpMethod.Post, "/systemone", body, Ok);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = (await ReadJson(response))["error"];
        Assert.Equal(expectedCode, (string)error["code"]);
        Assert.Equal(expectedPath, (string)error["path"]);
        Assert.Empty(context.Sent);
    }

    public static TheoryData<string, string, string> InvalidRequests()
    {
        static string Builder(params JObject[] rows) => new JObject
        {
            ["state"] = "s",
            ["definition"] = new JObject { ["mode"] = "Builder", ["questions"] = new JArray(rows) }
        }.ToString();

        static string NativeJson(string questionsJson) => new JObject
        {
            ["state"] = "s",
            ["definition"] = new JObject { ["mode"] = "JSON", ["questionsJson"] = questionsJson }
        }.ToString();

        static JObject Row(string id, string type) => new() { ["id"] = id, ["type"] = type };

        var tooManyOptions = new JObject();
        for (var i = 0; i < 256; i++)
        {
            tooManyOptions["o" + i] = null;
        }

        return new TheoryData<string, string, string>
        {
            { "", "missing_body", null },
            { "[1]", "invalid_body", null },
            { "{\"definition\":{}}", "missing_state", "state" },
            { "{\"state\":\"{oops\",\"stateFormat\":\"JSON\",\"definition\":{}}", "invalid_state_json", "state" },
            { "{\"state\":\"s\",\"stateFormat\":\"XML\",\"definition\":{}}", "invalid_state_format", "stateFormat" },
            { "{\"state\":5,\"definition\":{}}", "invalid_state", "state" },
            { "{\"state\":\"s\",\"model\":1,\"definition\":{}}", "invalid_model", "model" },
            { "{\"state\":\"s\"}", "missing_definition", "definition" },
            { "{\"state\":\"s\",\"definition\":{\"mode\":\"YAML\"}}", "invalid_mode", "definition.mode" },
            { Builder(), "missing_questions", "definition.questions" },
            { Builder(Row("a", "noul"), Row("a", "noul")), "duplicate_question_id", "definition.questions[1].id" },
            { Builder(Row(" ", "noul")), "missing_question_id", "definition.questions[0].id" },
            { Builder(Row("a", "rank")), "invalid_question_type", "definition.questions[0].type" },
            { Builder(Row("a", "choice")), "missing_choice_options", "definition.questions[0].choiceOptions" },
            {
                Builder(new JObject { ["id"] = "a", ["type"] = "choice", ["choiceOptions"] = new JArray(new JObject { ["key"] = "x" }, new JObject { ["key"] = "x" }) }),
                "duplicate_choice_key", "definition.questions[0].choiceOptions[1].key"
            },
            {
                Builder(new JObject { ["id"] = "a", ["type"] = "score", ["scoreLevels"] = new JArray("only one") }),
                "invalid_score_levels", "definition.questions[0].scoreLevels"
            },
            {
                Builder(new JObject { ["id"] = "a", ["type"] = "score", ["scoreLevels"] = new JArray(Enumerable.Range(0, 11).Select(i => "L" + i)) }),
                "invalid_score_levels", "definition.questions[0].scoreLevels"
            },
            {
                Builder(new JObject { ["id"] = "a", ["type"] = "noul", ["instructions"] = 3 }),
                "invalid_value", "definition.questions[0].instructions"
            },
            { NativeJson(""), "missing_questions", "definition.questionsJson" },
            { NativeJson("{not json"), "invalid_questions_json", "definition.questionsJson" },
            { NativeJson("[]"), "invalid_questions_json", "definition.questionsJson" },
            { NativeJson("{}"), "missing_questions", "definition.questionsJson" },
            { NativeJson("{\"a\":{\"type\":\"choice\",\"criteria\":" + tooManyOptions.ToString() + "}}"), "too_many_choice_options", "definition.questionsJson['a'].criteria" },
            { NativeJson("{\"a\":{\"type\":\"noul\",\"criteria\":{\"maybe\":\"x\"}}}"), "invalid_noul_criteria", "definition.questionsJson['a'].criteria.maybe" },
            { NativeJson("{\"a\":{\"type\":\"noul\",\"instructions\":true}}"), "invalid_instructions", "definition.questionsJson['a'].instructions" },
            { NativeJson("{\"a\":{\"type\":\"score\",\"criteria\":[\"x\",1]}}"), "invalid_criteria_description", "definition.questionsJson['a'].criteria[1]" },
            { NativeJson("{\"a\":\"noul\"}"), "invalid_question", "definition.questionsJson['a']" }
        };
    }

    [Fact]
    public async Task Choice_with_255_options_is_accepted()
    {
        var criteria = new JObject();
        for (var i = 0; i < 255; i++)
        {
            criteria["o" + i] = null;
        }

        var body = new JObject
        {
            ["state"] = "s",
            ["definition"] = new JObject { ["mode"] = "JSON", ["questionsJson"] = new JObject { ["a"] = new JObject { ["type"] = "choice", ["criteria"] = criteria } }.ToString() }
        };

        var (response, context) = await Harness.RunAsync("Evaluate", HttpMethod.Post, "/systemone", body.ToString(), Ok);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Single(context.Sent);
    }

    [Fact]
    public async Task Logs_do_not_contain_state_questions_or_api_key()
    {
        var (_, context) = await Harness.RunAsync("Evaluate", HttpMethod.Post, "/systemone", Harness.Fixture("builder-request.json"), Ok);
        var (_, failed) = await Harness.RunAsync("Evaluate", HttpMethod.Post, "/systemone", "{\"state\":\"top secret state\",\"definition\":{\"mode\":\"JSON\",\"questionsJson\":\"{bad\"}}", Ok);

        var logs = string.Join("\n", context.CapturingLogger.Messages.Concat(failed.CapturingLogger.Messages));
        Assert.NotEmpty(context.CapturingLogger.Messages);
        Assert.DoesNotContain(Harness.ApiKey, logs);
        Assert.DoesNotContain("checkout page", logs);
        Assert.DoesNotContain("top secret", logs);
        Assert.DoesNotContain("immediate response", logs);
    }

    internal static async Task<JObject> ReadJson(HttpResponseMessage response) => JObject.Parse(await response.Content.ReadAsStringAsync());
}
