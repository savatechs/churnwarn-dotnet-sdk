namespace ChurnWarn.Sdk;

/// <summary>Thrown when the Gateway returns a non-success status or the body cannot be read.</summary>
public sealed class ChurnWarnApiException : Exception
{
    public ChurnWarnApiException(int statusCode, string? responseBody, string message)
        : base(message)
    {
        StatusCode = statusCode;
        ResponseBody = responseBody;
    }

    public int StatusCode { get; }
    public string? ResponseBody { get; }
}
