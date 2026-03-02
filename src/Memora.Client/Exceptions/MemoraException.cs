namespace ManuHub.Memora.Exceptions;

/// <summary>
/// Exception thrown for any Memora server error responses.
/// </summary>
public class MemoraException : Exception
{
    public MemoraException(string message) : base(message) { }
}