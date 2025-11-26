namespace MeetingScribe.Web.Services;

public class VideoProcessingOptions
{
    public string PythonExecutablePath { get; set; } = "python";
    public string ScriptPath { get; set; } = "python/processor.py";
    public int TimeoutSeconds { get; set; } = 86400; // 24 hours
    public string WhisperModelSize { get; set; } = "large-v3";
    public string FfmpegPath { get; set; } = "ffmpeg";
    public string SummaryModel { get; set; } = "C:/Users/osvth/.llama/checkpoints/Llama3.1-8B-Instruct-hf";
    public int MaxSummaryTokens { get; set; } = 4000;
    public int MaxNewTokens { get; set; } = 2048;
}


