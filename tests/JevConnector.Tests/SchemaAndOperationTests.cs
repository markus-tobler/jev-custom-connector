using System.Net;
using System.Text;
using Newtonsoft.Json.Linq;
using Xunit;

namespace JevConnector.Tests;

public class SchemaTests
{
    private static async Task<JObject> GetSchemaAsync(string body)
    {
        var (response, context) = await Harness.RunAsync("GetOutputSchema", HttpMethod.Post, "/_connector/output-schema", body);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(context.Sent);
        return (JObject)(await EvaluateTests.ReadJson(response))["schema"];
    }

    [Fact]
    public async Task Schema_is_generated_without_calling_typesafe_and_names_every_output()
    {
        var schema = await GetSchemaAsync(Harness.Fixture("builder-request.json"));
        var answers = schema["properties"]["answers"]["properties"];

        Assert.Equal(new[] { "is_urgent", "category", "severity" }, ((JObject)answers).Properties().Select(p => p.Name));
        Assert.Equal("number", (string)answers["is_urgent"]["properties"]["noul"]["type"]);
        Assert.Equal(new[] { "billing", "engineering", "other" }, ((JObject)answers["category"]["properties"]["probabilities"]["properties"]).Properties().Select(p => p.Name));
        Assert.Equal(new[] { "0", "1", "2", "3" }, ((JObject)answers["severity"]["properties"]["legend"]["properties"]).Properties().Select(p => p.Name));
        Assert.Equal("string", (string)answers["severity"]["properties"]["legend"]["properties"]["3"]["type"]);
        Assert.Equal("integer", (string)schema["properties"]["usage"]["properties"]["input_tokens"]["type"]);
    }

    [Fact]
    public async Task Designer_token_names_do_not_repeat_words()
    {
        var schema = await GetSchemaAsync(Harness.Fixture("builder-request.json"));

        var names = DesignerTokenNames(schema, null).ToDictionary(t => t.Path, t => t.Name);

        Assert.Equal("Model", names["model"]);
        Assert.Equal("Input tokens", names["usage.input_tokens"]);
        Assert.Equal("is_urgent", names["answers.is_urgent"]);
        Assert.Equal("is_urgent type", names["answers.is_urgent.type"]);
        Assert.Equal("is_urgent yes probability", names["answers.is_urgent.noul"]);
        Assert.Equal("category choice", names["answers.category.choice"]);
        Assert.Equal("category confidence", names["answers.category.confidence"]);
        Assert.Equal("category probability", names["answers.category.probabilities"]);
        Assert.Equal("category probability billing", names["answers.category.probabilities.billing"]);
        Assert.Equal("severity score", names["answers.severity.score"]);
        Assert.Equal("severity legend 0", names["answers.severity.legend.0"]);
        Assert.Equal("severity probability 3", names["answers.severity.probabilities.3"]);
        foreach (var name in names.Values.Where(n => n.Length > 0))
        {
            var words = name.Split(' ');
            Assert.True(words.Distinct().Count() == words.Length, "Repeated word in token name: " + name);
        }
    }

    // Mirrors SchemaProcessor in Azure/LogicAppsUX: a token's summary is the x-ms-summary of every
    // enclosing object joined with its own x-ms-summary.
    private static IEnumerable<(string Path, string Name)> DesignerTokenNames(JToken schema, string summaryPrefix, string path = "")
    {
        var properties = schema["properties"] as JObject;
        if (properties == null)
        {
            yield break;
        }

        foreach (var property in properties.Properties())
        {
            var summary = (string)property.Value["x-ms-summary"];
            var name = string.IsNullOrEmpty(summaryPrefix) ? summary ?? string.Empty : summary == null ? string.Empty : summaryPrefix + " " + summary;
            var childPath = path.Length == 0 ? property.Name : path + "." + property.Name;
            yield return (childPath, name);
            var childPrefix = string.IsNullOrEmpty(summaryPrefix) ? summary : summary == null ? summaryPrefix : summaryPrefix + " " + summary;
            foreach (var child in DesignerTokenNames(property.Value, childPrefix, childPath))
            {
                yield return child;
            }
        }
    }

    [Fact]
    public async Task Documented_response_conforms_to_generated_schema()
    {
        var schema = await GetSchemaAsync(Harness.Fixture("builder-request.json"));

        AssertConforms(schema, JObject.Parse(Harness.Fixture("systemone-response.json")), "$");
    }

    [Fact]
    public async Task Questions_only_body_and_whole_request_body_produce_same_schema()
    {
        var request = JObject.Parse(Harness.Fixture("builder-request.json"));
        var fromWholeBody = await GetSchemaAsync(request.ToString());
        var fromQuestions = await GetSchemaAsync(new JObject { ["questions"] = request["questions"] }.ToString());

        Assert.True(JToken.DeepEquals(fromWholeBody, fromQuestions));
    }

    [Fact]
    public async Task Native_questions_schema_handles_structured_legend_and_unusual_keys()
    {
        var questions = new JObject
        {
            ["Größe / size.v2"] = new JObject { ["type"] = "score", ["criteria"] = new JArray("small", new JObject { ["label"] = "large" }, null) },
            ["with space"] = new JObject { ["type"] = "choice", ["criteria"] = new JObject { ["a.b"] = null, ["Ja/Nein"] = "x" } }
        };
        var schema = await GetSchemaAsync(new JObject
        {
            ["questions"] = questions.ToString()
        }.ToString());

        var answers = (JObject)schema["properties"]["answers"]["properties"];
        Assert.Equal(new[] { "Größe / size.v2", "with space" }, answers.Properties().Select(p => p.Name));
        var legend = answers["Größe / size.v2"]["properties"]["legend"]["properties"];
        Assert.Equal("string", (string)legend["0"]["type"]);
        Assert.Null(legend["1"]["type"]);
        Assert.Null(legend["2"]["type"]);
        Assert.Equal(new[] { "a.b", "Ja/Nein" }, ((JObject)answers["with space"]["properties"]["probabilities"]["properties"]).Properties().Select(p => p.Name));
    }

    [Theory]
    [InlineData("")]
    [InlineData("{}")]
    [InlineData("{\"questions\":[]}")]
    [InlineData("{\"questions\":\"@{variables('questions')}\"}")]
    [InlineData("{\"questions\":[{\"id\":\"a\",\"type\":\"score\",\"levels\":[\"x\"]}]}")]
    public async Task Unresolvable_definitions_return_generic_schema(string body)
    {
        var schema = await GetSchemaAsync(body);
        var answers = schema["properties"]["answers"];

        Assert.Equal("object", (string)answers["type"]);
        Assert.Null(answers["properties"]);
        Assert.StartsWith("Answers keyed by question ID. Named outputs appear", (string)answers["description"]);
        Assert.NotNull(schema["properties"]["usage"]);
    }

    private static void AssertConforms(JToken schema, JToken value, string path)
    {
        var type = (string)schema["type"];
        switch (type)
        {
            case "object":
                Assert.True(value.Type == JTokenType.Object, path + " should be an object");
                var properties = schema["properties"] as JObject;
                if (properties == null)
                {
                    return;
                }

                foreach (var property in ((JObject)value).Properties())
                {
                    var propertySchema = properties[property.Name];
                    Assert.True(propertySchema != null, path + "." + property.Name + " is missing from the schema");
                    AssertConforms(propertySchema, property.Value, path + "." + property.Name);
                }

                break;
            case "string":
                Assert.True(value.Type == JTokenType.String, path + " should be a string");
                break;
            case "number":
                Assert.True(value.Type is JTokenType.Float or JTokenType.Integer, path + " should be a number");
                break;
            case "integer":
                Assert.True(value.Type == JTokenType.Integer, path + " should be an integer");
                break;
            case null:
                break;
            default:
                Assert.Fail("Unexpected schema type " + type + " at " + path);
                break;
        }
    }
}

public class OperationTests
{
    [Fact]
    public async Task Base64_operation_ids_are_decoded()
    {
        var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes("GetOutputSchema"));
        var (response, context) = await Harness.RunAsync(encoded, HttpMethod.Post, "/_connector/output-schema", Harness.Fixture("builder-request.json"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull((await EvaluateTests.ReadJson(response))["schema"]["properties"]["answers"]["properties"]["is_urgent"]);
        Assert.Empty(context.Sent);
    }

    [Theory]
    [InlineData("DeleteEverything")]
    [InlineData("RGVsZXRlRXZlcnl0aGluZw==")]
    [InlineData(null)]
    public async Task Unknown_operations_are_rejected(string operationId)
    {
        var (response, context) = await Harness.RunAsync(operationId, HttpMethod.Post, "/systemone", "{}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("unknown_operation", (string)(await EvaluateTests.ReadJson(response))["error"]["code"]);
        Assert.Empty(context.Sent);
    }

    [Fact]
    public async Task ListModels_forwards_get_and_returns_response_unchanged()
    {
        var upstream = Harness.Json(HttpStatusCode.OK, "{\"models\":[{\"name\":\"jev-1.13.0\",\"description\":\"Jev\",\"release_date\":\"2025-01-01\"}]}");
        var (response, context) = await Harness.RunAsync("ListModels", HttpMethod.Get, "/models?ignored=1", null, _ => upstream);

        Assert.Same(upstream, response);
        var sent = Assert.Single(context.Sent);
        Assert.Equal(HttpMethod.Get, sent.Method);
        Assert.Equal("https://api.typesafe.ai/v1/models", sent.Uri.ToString());
        Assert.Equal("Bearer " + Harness.ApiKey, sent.Authorization);
        Assert.Null(sent.Body);
    }

    [Fact]
    public async Task GetModelChoices_lists_aliases_first_and_deduplicates()
    {
        var upstream = Harness.Json(HttpStatusCode.OK, "{\"models\":[{\"name\":\"jev-latest\"},{\"name\":\"jev-1.13.0\",\"release_date\":\"2025-06-01\"},{\"name\":\"\"},{\"name\":5}]}");
        var (response, context) = await Harness.RunAsync("GetModelChoices", HttpMethod.Get, "/_connector/model-choices", null, _ => upstream);

        Assert.Equal("https://api.typesafe.ai/v1/models", Assert.Single(context.Sent).Uri.ToString());
        var models = (JArray)(await EvaluateTests.ReadJson(response))["models"];
        Assert.Equal(new[] { "jev-latest", "jev-preview", "jev-1.13.0" }, models.Select(m => (string)m["name"]));
        Assert.Equal("jev-1.13.0 (2025-06-01)", (string)models[2]["title"]);
    }

    [Fact]
    public async Task GetModelChoices_passes_errors_through()
    {
        var upstream = Harness.Json(HttpStatusCode.Unauthorized, "{\"detail\":\"Invalid API key\"}");
        var (response, _) = await Harness.RunAsync("GetModelChoices", HttpMethod.Get, "/_connector/model-choices", null, _ => upstream);

        Assert.Same(upstream, response);
    }

    [Fact]
    public async Task GetModelChoices_tolerates_unexpected_model_payload()
    {
        var (response, _) = await Harness.RunAsync("GetModelChoices", HttpMethod.Get, "/_connector/model-choices", null, _ => Harness.Json(HttpStatusCode.OK, "not json"));

        var models = (JArray)(await EvaluateTests.ReadJson(response))["models"];
        Assert.Equal(new[] { "jev-latest", "jev-preview" }, models.Select(m => (string)m["name"]));
    }
}
