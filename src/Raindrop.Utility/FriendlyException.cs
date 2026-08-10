namespace Raindrop.Utility;

public class FriendlyException(string message = "", int code = 0) : Exception(message)
{
    public int Code { get; set; } = code;
}