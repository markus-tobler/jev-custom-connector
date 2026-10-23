using Microsoft.Extensions.Logging;

namespace ConnectorTests;

internal sealed record RecordedRequest(HttpMethod Method, Uri Uri, string Authorization, string Body, IReadOnlyDictionary<string, string> Headers);

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
        var headers = request.Headers.ToDictionary(h => h.Key, h => string.Join(",", h.Value), StringComparer.OrdinalIgnoreCase);
        this.Sent.Add(new RecordedRequest(request.Method, request.RequestUri, headers.GetValueOrDefault("Authorization"), body, headers));
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
