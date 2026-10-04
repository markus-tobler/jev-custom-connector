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
    public async Task Native_questions_object_and_json_state_are_preserved()
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

        foreach (var questionsValue in new JToken[] { questions, questions.ToString() })
        {
            var request = new JObject
            {
                ["state"] = "  {\"order\":{\"id\":7,\"total\":12.50}}\n",
                ["questions"] = questionsValue
            };

            var (_, context) = await Harness.RunAsync("Evaluate", HttpMethod.Post, "/systemone", request.ToString(), Ok);

            var body = Assert.Single(context.Sent).Body;
            var payload = JObject.Parse(body);
            Assert.True(JToken.DeepEquals(questions, payload["questions"]), payload.ToString());
            Assert.Equal(JTokenType.Object, payload["state"].Type);
            Assert.Contains("\"total\":12.50", body);
            Assert.Equal("jev-latest", (string)payload["model"]);
        }
    }

    [Fact]
    public async Task Question_rows_can_be_passed_as_json_text()
    {
        var body = JObject.Parse(Harness.Fixture("builder-request.json"));
        body["questions"] = body["questions"].ToString();

        var (_, context) = await Harness.RunAsync("Evaluate", HttpMethod.Post, "/systemone", body.ToString(), Ok);

        var payload = JObject.Parse(Assert.Single(context.Sent).Body);
        Assert.True(JToken.DeepEquals(JObject.Parse(Harness.Fixture("builder-expected-payload.json"))["questions"], payload["questions"]), payload.ToString());
    }

    [Theory]
    [InlineData("{\"a\":1}", JTokenType.Object)]
    [InlineData("[1, 2]", JTokenType.Array)]
    [InlineData("{not json", JTokenType.String)]
    [InlineData("{\"a\":1} trailing", JTokenType.String)]
    [InlineData("42", JTokenType.String)]
    [InlineData("true", JTokenType.String)]
    [InlineData("\"quoted\"", JTokenType.String)]
    [InlineData("[urgent] server down", JTokenType.String)]
    public async Task State_text_is_sent_as_json_only_for_objects_and_arrays(string state, JTokenType expected)
    {
        var body = JObject.Parse(Harness.Fixture("builder-request.json"));
        body["state"] = state;

        var (_, context) = await Harness.RunAsync("Evaluate", HttpMethod.Post, "/systemone", body.ToString(), Ok);

        var sentState = JObject.Parse(Assert.Single(context.Sent).Body)["state"];
        Assert.Equal(expected, sentState.Type);
        if (expected == JTokenType.String)
        {
            Assert.Equal(state, (string)sentState);
        }
    }

    [Fact]
    public async Task Direct_callers_can_send_object_state_and_native_questions_object()
    {
        var body = new JObject
        {
            ["state"] = new JArray(new JObject { ["role"] = "user", ["text"] = "hi" }),
            ["model"] = "  ",
            ["questions"] = new JObject { ["greeting"] = new JObject { ["type"] = "noul", ["instructions"] = "Is it a greeting?" } }
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
            ["questions"] = new JArray(rows)
        }.ToString();

        static string NativeJson(string questionsJson) => new JObject
        {
            ["state"] = "s",
            ["questions"] = questionsJson
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
            { "{\"questions\":[]}", "missing_state", "state" },
            { "{\"state\":5,\"questions\":[]}", "invalid_state", "state" },
            { "{\"state\":\"s\",\"model\":1,\"questions\":[]}", "invalid_model", "model" },
            { "{\"state\":\"s\"}", "missing_questions", "questions" },
            { "{\"state\":\"s\",\"questions\":7}", "invalid_questions", "questions" },
            { Builder(), "missing_questions", "questions" },
            { Builder(Row("a", "noul"), Row("a", "noul")), "duplicate_question_id", "questions[1].id" },
            { Builder(Row(" ", "noul")), "missing_question_id", "questions[0].id" },
            { Builder(Row("a", "rank")), "invalid_question_type", "questions[0].type" },
            { Builder(Row("a", "choice")), "missing_choice_options", "questions[0].options" },
            {
                Builder(new JObject { ["id"] = "a", ["type"] = "choice", ["options"] = new JArray(new JObject { ["key"] = "x" }) }),
                "missing_choice_key", "questions[0].options[0].option"
            },
            {
                Builder(new JObject { ["id"] = "a", ["type"] = "choice", ["options"] = new JArray("billing") }),
                "invalid_choice_option", "questions[0].options[0]"
            },
            {
                Builder(new JObject { ["id"] = "a", ["type"] = "choice", ["options"] = new JArray(new JObject { ["option"] = "x" }, new JObject { ["option"] = "x", ["description"] = "again" }) }),
                "duplicate_choice_key", "questions[0].options[1].option"
            },
            {
                Builder(new JObject { ["id"] = "a", ["type"] = "score", ["levels"] = new JArray("only one") }),
                "invalid_score_levels", "questions[0].levels"
            },
            {
                Builder(new JObject { ["id"] = "a", ["type"] = "score", ["levels"] = new JArray(Enumerable.Range(0, 11).Select(i => "L" + i)) }),
                "invalid_score_levels", "questions[0].levels"
            },
            {
                Builder(new JObject { ["id"] = "a", ["type"] = "noul", ["instructions"] = 3 }),
                "invalid_value", "questions[0].instructions"
            },
            { NativeJson(""), "missing_questions", "questions" },
            { NativeJson("{not json"), "invalid_questions_json", "questions" },
            { NativeJson("[]"), "missing_questions", "questions" },
            { NativeJson("{}"), "missing_questions", "questions" },
            { NativeJson("{\"a\":{\"type\":\"choice\",\"criteria\":" + tooManyOptions.ToString() + "}}"), "too_many_choice_options", "questions['a'].criteria" },
            { NativeJson("{\"a\":{\"type\":\"noul\",\"criteria\":{\"maybe\":\"x\"}}}"), "invalid_noul_criteria", "questions['a'].criteria.maybe" },
            { NativeJson("{\"a\":{\"type\":\"noul\",\"instructions\":true}}"), "invalid_instructions", "questions['a'].instructions" },
            { NativeJson("{\"a\":{\"type\":\"score\",\"criteria\":[\"x\",1]}}"), "invalid_criteria_description", "questions['a'].criteria[1]" },
            { NativeJson("{\"a\":\"noul\"}"), "invalid_question", "questions['a']" }
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
            ["questions"] = new JObject { ["a"] = new JObject { ["type"] = "choice", ["criteria"] = criteria } }
        };

        var (response, context) = await Harness.RunAsync("Evaluate", HttpMethod.Post, "/systemone", body.ToString(), Ok);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Single(context.Sent);
    }

    [Fact]
    public async Task Logs_do_not_contain_state_questions_or_api_key()
    {
        var (_, context) = await Harness.RunAsync("Evaluate", HttpMethod.Post, "/systemone", Harness.Fixture("builder-request.json"), Ok);
        var (_, failed) = await Harness.RunAsync("Evaluate", HttpMethod.Post, "/systemone", "{\"state\":\"top secret state\",\"questions\":\"{bad\"}", Ok);

        var logs = string.Join("\n", context.CapturingLogger.Messages.Concat(failed.CapturingLogger.Messages));
        Assert.NotEmpty(context.CapturingLogger.Messages);
        Assert.DoesNotContain(Harness.ApiKey, logs);
        Assert.DoesNotContain("checkout page", logs);
        Assert.DoesNotContain("top secret", logs);
        Assert.DoesNotContain("immediate response", logs);
    }

    internal static async Task<JObject> ReadJson(HttpResponseMessage response) => JObject.Parse(await response.Content.ReadAsStringAsync());
}
