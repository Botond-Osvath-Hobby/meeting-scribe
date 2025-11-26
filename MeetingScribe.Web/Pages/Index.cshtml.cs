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

    public VideoProcessingResult? Result { get; private set; }

    public string? ErrorMessage { get; private set; }

    public string? FormattedSummary => Result?.BusinessSummary is not null 
        ? Markdown.ToHtml(Result.BusinessSummary, new MarkdownPipelineBuilder().UseAdvancedExtensions().Build())
        : null;

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
            Result = await _videoProcessing.ProcessAsync(tempFile, OperationId, cancellationToken);
        }
        catch (VideoProcessingException ex)
        {
            ErrorMessage = ex.Message;
            _progressTracker.UpdateStage(OperationId, ProgressStageKeys.Summarize, ProgressStates.Failed, ex.Message);
            _logger.LogWarning(ex, "Video processing failed with a known error.");
        }
        catch (Exception ex)
        {
            ErrorMessage = "We could not process the video. Check the server logs for details.";
            _progressTracker.UpdateStage(OperationId, ProgressStageKeys.Summarize, ProgressStates.Failed, ErrorMessage);
            _logger.LogError(ex, "Unexpected processing failure.");
        }
        finally
        {
            System.IO.File.Delete(tempFile);
        }

        return Respond();
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
                result = Result
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
}
