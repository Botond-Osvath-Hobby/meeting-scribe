namespace MeetingScribe.Web.Services;

public class VideoProcessingException : Exception
{
    public VideoProcessingException(string message)
        : base(message)
    {
    }

    public VideoProcessingException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}


