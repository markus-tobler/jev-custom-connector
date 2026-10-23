
/// <summary>
/// Local stand-in for the Power Platform custom code base class, matching the documented surface:
/// https://learn.microsoft.com/connectors/custom-connectors/write-code
/// </summary>
public abstract class ScriptBase
{
    public IScriptContext Context { get; internal set; }

    public CancellationToken CancellationToken { get; internal set; }

    public static StringContent CreateJsonContent(string serializedJson)
    {
        return new StringContent(serializedJson, Encoding.UTF8, "application/json");
    }

    public abstract Task<HttpResponseMessage> ExecuteAsync();

    internal void Initialize(IScriptContext context, CancellationToken cancellationToken)
    {
        this.Context = context;
        this.CancellationToken = cancellationToken;
    }
}

public interface IScriptContext
{
    string CorrelationId { get; }

    string OperationId { get; }

    HttpRequestMessage Request { get; }

    ILogger Logger { get; }

    Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken);
}
