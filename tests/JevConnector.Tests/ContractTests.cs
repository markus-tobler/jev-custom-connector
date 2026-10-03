using System.Reflection;
using System.Xml.Linq;
using Newtonsoft.Json.Linq;
using Xunit;

namespace JevConnector.Tests;

public class ContractTests
{
    private static readonly JObject Swagger = JObject.Parse(Harness.Connector("mto_jevv1connector_openapidefinition.json"));

    private static IEnumerable<JProperty> Operations() =>
        ((JObject)Swagger["paths"]).Properties()
            .SelectMany(path => ((JObject)path.Value).Properties())
            .Where(op => op.Name is "get" or "post" or "put" or "patch" or "delete");

    private static JObject Operation(string operationId) =>
        (JObject)Operations().Single(op => (string)op.Value["operationId"] == operationId).Value;

    [Fact]
    public void Script_operations_match_openapi_and_script()
    {
        var swaggerIds = Operations().Select(op => (string)op.Value["operationId"]).OrderBy(id => id).ToArray();

        var connectorXml = XDocument.Parse(Harness.Connector("mto_jevv1connector.xml"));
        var scriptOperations = JArray.Parse(connectorXml.Root.Element("scriptoperations").Value).Select(t => (string)t).OrderBy(id => id).ToArray();

        var known = ((string[])typeof(Script).GetField("KnownOperations", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null)).OrderBy(id => id).ToArray();

        Assert.Equal(swaggerIds, scriptOperations);
        Assert.Equal(swaggerIds, known);
    }

    [Fact]
    public void Evaluate_response_uses_connector_generated_dynamic_schema()
    {
        var schema = Operation("Evaluate")["responses"]["200"]["schema"];

        Assert.Equal("GetOutputSchema", (string)schema["x-ms-dynamic-schema"]["operationId"]);
        Assert.Equal("body", (string)schema["x-ms-dynamic-schema"]["parameters"]["body"]["parameter"]);
        Assert.Equal("schema", (string)schema["x-ms-dynamic-schema"]["value-path"]);
        Assert.Equal("GetOutputSchema", (string)schema["x-ms-dynamic-properties"]["operationId"]);
        Assert.Equal("body/definition", (string)schema["x-ms-dynamic-properties"]["parameters"]["body/definition"]["parameterReference"]);
        Assert.Equal("schema", (string)schema["x-ms-dynamic-properties"]["itemValuePath"]);

        var schemaOperation = Operation("GetOutputSchema");
        Assert.Equal("internal", (string)schemaOperation["x-ms-visibility"]);
        Assert.Equal("body", (string)schemaOperation["parameters"][0]["name"]);
        Assert.Equal("#/definitions/OutputSchemaRequest", (string)schemaOperation["parameters"][0]["schema"]["$ref"]);
        Assert.NotNull(Swagger["definitions"]["OutputSchemaRequest"]["properties"]["definition"]);
        Assert.NotNull(Swagger["definitions"]["EvaluateRequest"]["properties"]["definition"]);
    }

    [Fact]
    public void Model_picker_uses_internal_choices_operation()
    {
        var model = Swagger["definitions"]["EvaluateRequest"]["properties"]["model"];

        Assert.Equal("jev-latest", (string)model["default"]);
        Assert.Equal("GetModelChoices", (string)model["x-ms-dynamic-values"]["operationId"]);
        Assert.Equal("GetModelChoices", (string)model["x-ms-dynamic-list"]["operationId"]);
        Assert.Equal("models", (string)model["x-ms-dynamic-list"]["itemsPath"]);
        Assert.Equal("internal", (string)Operation("GetModelChoices")["x-ms-visibility"]);
    }

    [Fact]
    public void Api_key_is_sent_in_authorization_header()
    {
        var apiKey = Swagger["securityDefinitions"]["api_key"];

        Assert.Equal("apiKey", (string)apiKey["type"]);
        Assert.Equal("header", (string)apiKey["in"]);
        Assert.Equal("Authorization", (string)apiKey["name"]);
        Assert.Equal("api.typesafe.ai", (string)Swagger["host"]);
        Assert.Equal("/v1", (string)Swagger["basePath"]);
    }

    [Fact]
    public void Definition_avoids_unsupported_constructs_and_size_limits()
    {
        var forbidden = new[] { "allOf", "anyOf", "oneOf", "uniqueItems" };
        var used = Swagger.Descendants().OfType<JProperty>().Select(p => p.Name).Where(forbidden.Contains).ToArray();

        Assert.Empty(used);
        Assert.Equal("2.0", (string)Swagger["swagger"]);
        Assert.True(Harness.Connector("mto_jevv1connector_openapidefinition.json").Length < 1024 * 1024);
        Assert.True(Harness.Connector("mto_jevv1connector_customcodeblobcontent.csx").Length < 1024 * 1024);
    }
}
