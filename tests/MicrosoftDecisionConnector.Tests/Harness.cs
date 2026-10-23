using System.Net;
using System.Text;

namespace MicrosoftDecisionConnector.Tests;

internal static class Harness
{
    public const string ApiKey = "foundry-secret-key-123";
    public const string Deployment = "triage-decision";
    public const string Endpoint = "https://my-resource.services.ai.azure.com";
    public const string SystemOneUrl = Endpoint + "/providers/microsoft/v1/systemone";

    // Mirrors the request custom code receives after the connector policies have routed it to the
    // connection's endpoint and copied the deployment name into a header.
    public static async Task<(HttpResponseMessage Response, FakeScriptContext Context)> RunAsync(
        string operationId,
        HttpMethod method,
        string path,
        string body = null,
        Func<HttpRequestMessage, HttpResponseMessage> responder = null,
        string apiKey = ApiKey,
        string deployment = Deployment,
        string endpoint = Endpoint)
    {
        var request = new HttpRequestMessage(method, endpoint + "/providers/microsoft/v1" + path);
        if (apiKey != null)
        {
            request.Headers.TryAddWithoutValidation("api-key", apiKey);
        }

        if (deployment != null)
        {
            request.Headers.TryAddWithoutValidation("x-connector-deployment", deployment);
        }

        if (body != null)
        {
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        }

        var context = new FakeScriptContext(operationId, request, responder);
        var script = new Script();
        script.Initialize(context, CancellationToken.None);
        var response = await script.ExecuteAsync();
        return (response, context);
    }

    public static HttpResponseMessage Json(HttpStatusCode status, string json)
    {
        return new HttpResponseMessage(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
    }

    public static string Fixture(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));

    public static string Connector(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Connector", name));
}
