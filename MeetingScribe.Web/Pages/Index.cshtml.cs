using System.IO;
using System.Linq;
using Markdig;
using MeetingScribe.Web.Models;
using MeetingScribe.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace MeetingScribe.Web.Pages;

public class IndexModel : PageModel
{
    private static readonly string[] VideoExtensions =
    [
        ".mp4", ".mov", ".mkv", ".avi", ".webm"
    ];

    private static readonly string[] AudioExtensions =
    [
        ".mp3", ".wav", ".m4a", ".aac", ".flac", ".ogg"
    ];

    private static readonly HashSet<string> AllowedExtensions =
        new(VideoExtensions.Concat(AudioExtensions), StringComparer.OrdinalIgnoreCase);

    private readonly IWebHostEnvironment _environment;
    private readonly VideoProcessingService _videoProcessing;
    private readonly ProcessingProgressTracker _progressTracker;
    private readonly ILogger<IndexModel> _logger;

    public IndexModel(
        IWebHostEnvironment environment,
        VideoProcessingService videoProcessing,
        ProcessingProgressTracker progressTracker,
        ILogger<IndexModel> logger)
    {
        _environment = environment;
        _videoProcessing = videoProcessing;
        _progressTracker = progressTracker;
        _logger = logger;
    }

    [BindProperty]
    public IFormFile? MeetingVideo { get; set; }

    [BindProperty]
    public string? OperationId { get; set; }

    [BindProperty]
    public string? TranscriptPath { get; set; }

    [BindProperty]
    public string? SystemPrompt { get; set; }

    [BindProperty]
    public string? UserPromptTemplate { get; set; }

    [BindProperty]
    public string? CriticalInstruction { get; set; }

    public VideoProcessingResult? Result { get; private set; }

    public TranscriptData? Transcript { get; private set; }

    public string? ErrorMessage { get; private set; }

    public string? FormattedSummary => Result?.BusinessSummary is not null 
        ? Markdown.ToHtml(Result.BusinessSummary, new MarkdownPipelineBuilder().UseAdvancedExtensions().Build())
        : null;

    public IReadOnlyList<AgentPreset> AgentPresets => Models.AgentPresets.All;

    private bool IsAjaxRequest =>
        string.Equals(Request?.Headers["X-Requested-With"], "XMLHttpRequest", StringComparison.OrdinalIgnoreCase);

    public void OnGet() => OperationId ??= Guid.NewGuid().ToString("N");

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (!ValidateUpload())
        {
            return Respond();
        }

        OperationId = string.IsNullOrWhiteSpace(OperationId)
            ? Guid.NewGuid().ToString("N")
            : OperationId;

        _progressTracker.Start(OperationId);
        _progressTracker.UpdateStage(OperationId, ProgressStageKeys.Upload, ProgressStates.Running, "Uploading file");
        var uploadsRoot = Path.Combine(_environment.ContentRootPath, "App_Data", "uploads");
        Directory.CreateDirectory(uploadsRoot);
        var extension = Path.GetExtension(MeetingVideo!.FileName).ToLowerInvariant();
        var tempFile = Path.Combine(uploadsRoot, $"{Guid.NewGuid():N}{extension}");

        await using (var fileStream = System.IO.File.Create(tempFile))
        {
            await MeetingVideo.CopyToAsync(fileStream, cancellationToken);
        }

        _progressTracker.UpdateStage(OperationId, ProgressStageKeys.Upload, ProgressStates.Completed, "Upload completed");
        _progressTracker.UpdateStage(OperationId, ProgressStageKeys.Transcribe, ProgressStates.Running, "Starting AI pipeline");

        try
        {
            // Step 1 & 2: Transcribe and save transcript only
            Transcript = await _videoProcessing.TranscribeOnlyAsync(tempFile, OperationId, cancellationToken);
            TranscriptPath = Transcript.TranscriptPath;

            // Mark transcription as complete - user will choose agent preset next
            _progressTracker.UpdateStage(OperationId, ProgressStageKeys.Transcribe, ProgressStates.Completed, "Transcription complete. Ready to generate summary.");
            
            // Don't automatically run summarization - wait for user to select agent and click "Generate Summary"
        }
        catch (VideoProcessingException ex)
        {
            ErrorMessage = ex.Message;
            _progressTracker.UpdateStage(OperationId, ProgressStageKeys.Transcribe, ProgressStates.Failed, ex.Message);
            _logger.LogWarning(ex, "Video processing failed with a known error.");
        }
        catch (Exception ex)
        {
            ErrorMessage = "We could not process the video. Check the server logs for details.";
            _progressTracker.UpdateStage(OperationId, ProgressStageKeys.Transcribe, ProgressStates.Failed, ErrorMessage);
            _logger.LogError(ex, "Unexpected processing failure.");
        }
        finally
        {
            System.IO.File.Delete(tempFile);
        }

        return Respond();
    }

    public async Task<IActionResult> OnPostGenerateSummaryAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(TranscriptPath) || !System.IO.File.Exists(TranscriptPath))
        {
            ErrorMessage = "Transcript file not found. Please upload a video first.";
            return Respond();
        }

        if (string.IsNullOrWhiteSpace(SystemPrompt) || string.IsNullOrWhiteSpace(UserPromptTemplate) || string.IsNullOrWhiteSpace(CriticalInstruction))
        {
            ErrorMessage = "System prompt, user prompt template, and critical instruction are required.";
            return Respond();
        }

        OperationId = string.IsNullOrWhiteSpace(OperationId)
            ? Guid.NewGuid().ToString("N")
            : OperationId;

        _progressTracker.Start(OperationId);
        _progressTracker.UpdateStage(OperationId, ProgressStageKeys.Summarize, ProgressStates.Running, "Generating summary with selected agent");

        try
        {
            // Load the transcript data to get notes
            var transcriptJson = await System.IO.File.ReadAllTextAsync(TranscriptPath, cancellationToken);
            var transcriptData = System.Text.Json.JsonSerializer.Deserialize<TranscriptDataJson>(transcriptJson);
            
            Result = await _videoProcessing.SummarizeFromTranscriptAsync(
                TranscriptPath,
                SystemPrompt,
                UserPromptTemplate,
                CriticalInstruction,
                OperationId,
                cancellationToken
            );

            // Update result with the notes from transcript
            if (transcriptData?.Notes != null)
            {
                Result = Result with { Notes = transcriptData.Notes };
            }
        }
        catch (VideoProcessingException ex)
        {
            ErrorMessage = ex.Message;
            _progressTracker.UpdateStage(OperationId, ProgressStageKeys.Summarize, ProgressStates.Failed, ex.Message);
            _logger.LogWarning(ex, "Summary generation failed with a known error.");
        }
        catch (Exception ex)
        {
            ErrorMessage = "We could not generate the summary. Check the server logs for details.";
            _progressTracker.UpdateStage(OperationId, ProgressStageKeys.Summarize, ProgressStates.Failed, ErrorMessage);
            _logger.LogError(ex, "Unexpected generation failure.");
        }

        return Respond();
    }

    public IActionResult OnGetAgentPresets()
    {
        return new JsonResult(Models.AgentPresets.All.Select(p => new
        {
            p.Id,
            p.Name,
            p.Description,
            p.SystemPrompt,
            p.UserPromptTemplate
        }));
    }

    private IActionResult Respond()
    {
        if (IsAjaxRequest)
        {
            var errors = ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage).ToList();
            return new JsonResult(new
            {
                success = errors.Count == 0 && ErrorMessage is null,
                errors,
                errorMessage = ErrorMessage,
                result = Result,
                transcript = Transcript,
                transcriptPath = Transcript?.TranscriptPath ?? TranscriptPath
            });
        }

        return Page();
    }

    private bool ValidateUpload()
    {
        if (MeetingVideo is null || MeetingVideo.Length == 0)
        {
            ModelState.AddModelError(nameof(MeetingVideo), "Please upload a meeting recording.");
            return false;
        }

        var extension = Path.GetExtension(MeetingVideo.FileName).ToLowerInvariant();
        if (!AllowedExtensions.Contains(extension))
        {
            ModelState.AddModelError(nameof(MeetingVideo), "Unsupported media type. Upload common video files (MP4, MOV, MKV, AVI, WEBM) or audio files (MP3, WAV, M4A, FLAC, OGG).");
        }

        if (MeetingVideo.Length > 2L * 1024 * 1024 * 1024)
        {
            ModelState.AddModelError(nameof(MeetingVideo), "Videos must be 2 GB or smaller.");
        }

        return ModelState.IsValid;
    }

    private record TranscriptDataJson(List<string>? Notes, string? TranscriptText);
}
