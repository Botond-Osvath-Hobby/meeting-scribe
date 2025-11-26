namespace MeetingScribe.Web.Models;

public record VideoProcessingResult(
    IReadOnlyList<string> Notes, 
    string BusinessSummary,
    TimeSpan ProcessingDuration);


