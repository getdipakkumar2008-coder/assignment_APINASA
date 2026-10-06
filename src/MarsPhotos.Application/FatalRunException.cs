namespace MarsPhotos.Application;

/// <summary>A condition that prevents the run from starting or continuing (missing key, unreadable input, unwritable output).</summary>
public sealed class FatalRunException(string errorCode, string message, Exception? inner = null) : Exception(message, inner)
{
    public string ErrorCode { get; } = errorCode;
}
