namespace MeetingScribe.Web.Services;

public class VideoProcessingOptions
{
    public string PythonExecutablePath { get; set; } = "python";
    public string ScriptPath { get; set; } = "python/processor.py";
    public int TimeoutSeconds { get; set; } = 900;
}


