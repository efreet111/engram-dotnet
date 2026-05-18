namespace Engram.Sync.Transport;

/// <summary>
/// Exception thrown when mutation transport encounters an error.
/// Includes HTTP status code and error details from server response.
/// </summary>
public sealed class MutationTransportException : Exception
{
    /// <summary>
    /// HTTP status code from server response.
    /// </summary>
    public int StatusCode { get; }

    /// <summary>
    /// Error class from server (e.g., "policy", "validation").
    /// </summary>
    public string? ErrorClass { get; }

    /// <summary>
    /// Error code from server (e.g., "sync-paused", "invalid-payload").
    /// </summary>
    public string? ErrorCode { get; }

    /// <summary>
    /// Raw response body from server.
    /// </summary>
    public string? ResponseBody { get; }

    public MutationTransportException(int statusCode, string? errorClass, string? errorCode, string? responseBody)
        : base($"Mutation transport failed with status {statusCode}. Error: {errorCode ?? "unknown"}")
    {
        StatusCode = statusCode;
        ErrorClass = errorClass;
        ErrorCode = errorCode;
        ResponseBody = responseBody;
    }

    public MutationTransportException(int statusCode, string message)
        : base(message)
    {
        StatusCode = statusCode;
    }
}
