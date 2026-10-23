using System.Net;
using System.Text;

namespace JevConnector.Tests;

internal static class Harness
{
    public const string ApiKey = "ts-secret-key-123";

    public static async Task<(HttpResponseMessage Response, FakeScriptContext Context)> RunAsync(
        string operationId,
        HttpMethod method,
        string path,
        string body = null,
        Func<HttpRequestMessage, HttpResponseMessage> responder = null,
        string authorization = ApiKey)
    {
        var request = new HttpRequestMessage(method, "https://api.typesafe.ai/v1" + path);
        if (authorization != null)
        {
            request.Headers.TryAddWithoutValidation("Authorization", authorization);
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
