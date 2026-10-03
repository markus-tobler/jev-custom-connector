using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.Extensions.Logging;

namespace JevConnector.Tests;

internal sealed record RecordedRequest(HttpMethod Method, Uri Uri, string Authorization, string Body);

internal sealed class FakeScriptContext : IScriptContext
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> responder;

    public FakeScriptContext(string operationId, HttpRequestMessage request, Func<HttpRequestMessage, HttpResponseMessage> responder = null)
    {
        this.OperationId = operationId;
        this.Request = request;
        this.responder = responder ?? (_ => throw new InvalidOperationException("No outbound call was expected."));
    }

    public string CorrelationId => "test-correlation";

    public string OperationId { get; }

    public HttpRequestMessage Request { get; }

    public CapturingLogger CapturingLogger { get; } = new();

    public ILogger Logger => this.CapturingLogger;

    public List<RecordedRequest> Sent { get; } = new();

    public async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content == null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        var authorization = request.Headers.TryGetValues("Authorization", out var values) ? values.Single() : null;
        this.Sent.Add(new RecordedRequest(request.Method, request.RequestUri, authorization, body));
        return this.responder(request);
    }
}

internal sealed class CapturingLogger : ILogger
{
    public List<string> Messages { get; } = new();

    public IDisposable BeginScope<TState>(TState state) => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
    {
        this.Messages.Add(formatter(state, exception));
    }
}

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
