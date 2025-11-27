namespace MeetingScribe.Web.Models;

public record TranscriptData(
    IReadOnlyList<string> Notes,
    string TranscriptText,
    string TranscriptPath
);


