using System.Reflection;
using System.Xml.Linq;
using Newtonsoft.Json.Linq;
using Xunit;

namespace MicrosoftDecisionConnector.Tests;

public class ContractTests
{
    private static readonly JObject Swagger = JObject.Parse(Harness.Connector("mto_msdecision1connector_openapidefinition.json"));

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

        var connectorXml = XDocument.Parse(Harness.Connector("mto_msdecision1connector.xml"));
        var scriptOperations = JArray.Parse(connectorXml.Root.Element("scriptoperations").Value).Select(t => (string)t).OrderBy(id => id).ToArray();

        var known = ((string[])typeof(Script).GetField("KnownOperations", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null)).OrderBy(id => id).ToArray();

        Assert.Equal(new[] { "Evaluate", "EvaluateJson", "GetOutputSchema" }, swaggerIds);
        Assert.Equal(swaggerIds, scriptOperations);
        Assert.Equal(swaggerIds, known);
    }

    [Fact]
    public void Evaluate_response_uses_connector_generated_dynamic_schema()
    {
        var schema = Operation("Evaluate")["responses"]["200"]["schema"];

        Assert.Equal("GetOutputSchema", (string)schema["x-ms-dynamic-schema"]["operationId"]);
        Assert.Equal("questions", (string)schema["x-ms-dynamic-schema"]["parameters"]["questions"]["parameter"]);
        Assert.Equal("schema", (string)schema["x-ms-dynamic-schema"]["value-path"]);
        Assert.Equal("GetOutputSchema", (string)schema["x-ms-dynamic-properties"]["operationId"]);
        Assert.Equal("body/questions", (string)schema["x-ms-dynamic-properties"]["parameters"]["body/questions"]["parameterReference"]);
        Assert.Equal("schema", (string)schema["x-ms-dynamic-properties"]["itemValuePath"]);

        var schemaOperation = Operation("GetOutputSchema");
        Assert.Equal("internal", (string)schemaOperation["x-ms-visibility"]);
        Assert.Equal("#/definitions/OutputSchemaRequest", (string)schemaOperation["parameters"][0]["schema"]["$ref"]);
        var option = (JObject)Swagger["definitions"]["ChoiceOption"]["properties"];
        Assert.Null(option["key"]); // The designer's array editor hides item properties named "key".
        Assert.NotNull(option["option"]);
        // The deployment comes from the connection, so the action has no model input.
        Assert.Equal(new[] { "state", "questions" }, ((JObject)Swagger["definitions"]["EvaluateRequest"]["properties"]).Properties().Select(p => p.Name));
    }

    [Fact]
    public void Api_key_is_sent_in_api_key_header()
    {
        var apiKey = Swagger["securityDefinitions"]["api_key"];

        Assert.Equal("apiKey", (string)apiKey["type"]);
        Assert.Equal("header", (string)apiKey["in"]);
        Assert.Equal("api-key", (string)apiKey["name"]);
        Assert.Equal("/providers/microsoft/v1", (string)Swagger["basePath"]);
    }

    [Fact]
    public void Connection_and_policies_route_to_endpoint_and_deployment()
    {
        var parameters = JObject.Parse(Harness.Connector("mto_msdecision1connector_connectionparameters.json"));
        Assert.Equal(new[] { "endpoint", "deployment", "api_key" }, parameters.Properties().Select(p => p.Name));
        Assert.Equal("securestring", (string)parameters["api_key"]["type"]);
        Assert.All(parameters.Properties(), p => Assert.Equal("true", (string)p.Value["uiDefinition"]["constraints"]["required"]));

        var policies = JArray.Parse(Harness.Connector("mto_msdecision1connector_policytemplateinstances.json"));
        var host = Assert.Single(policies, p => (string)p["TemplateId"] == "dynamichosturl");
        Assert.Equal("@connectionParameters('endpoint')", (string)host["Parameters"]["x-ms-apimTemplateParameter.urlTemplate"]);
        var header = Assert.Single(policies, p => (string)p["TemplateId"] == "setheader");
        Assert.Equal("x-connector-deployment", (string)header["Parameters"]["x-ms-apimTemplateParameter.name"]);
        Assert.Equal("@connectionParameters('deployment')", (string)header["Parameters"]["x-ms-apimTemplateParameter.value"]);
        Assert.Equal("Request", (string)header["Parameters"]["x-ms-apimTemplate-policySection"]);
    }

    [Fact]
    public void Definition_avoids_unsupported_constructs_and_size_limits()
    {
        var forbidden = new[] { "allOf", "anyOf", "oneOf", "uniqueItems" };
        var used = Swagger.Descendants().OfType<JProperty>().Select(p => p.Name).Where(forbidden.Contains).ToArray();

        Assert.Empty(used);
        Assert.Equal("2.0", (string)Swagger["swagger"]);
        Assert.True(Harness.Connector("mto_msdecision1connector_openapidefinition.json").Length < 1024 * 1024);
        Assert.True(Harness.Connector("mto_msdecision1connector_customcodeblobcontent.csx").Length < 1024 * 1024);
    }
}
