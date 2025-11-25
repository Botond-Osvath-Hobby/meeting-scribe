namespace MeetingScribe.Web.Services;

public class VideoProcessingOptions
{
    public string PythonExecutablePath { get; set; } = "python";
    public string ScriptPath { get; set; } = "python/processor.py";
    public int TimeoutSeconds { get; set; } = 7200;
    public string WhisperModelSize { get; set; } = "large-v3";
    public string FfmpegPath { get; set; } = "ffmpeg";
    public string SummaryModel { get; set; } = "Szumis/HuBERT-XL-captions";
}


