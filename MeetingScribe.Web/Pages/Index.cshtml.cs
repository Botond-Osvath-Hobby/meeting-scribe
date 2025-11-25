using System.IO;
using MeetingScribe.Web.Models;
using MeetingScribe.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace MeetingScribe.Web.Pages;

public class IndexModel : PageModel
{
    private static readonly string[] AllowedExtensions =
    [
        ".mp4", ".mov", ".mkv", ".avi", ".webm"
    ];

    private readonly IWebHostEnvironment _environment;
    private readonly VideoProcessingService _videoProcessing;
    private readonly ILogger<IndexModel> _logger;

    public IndexModel(
        IWebHostEnvironment environment,
        VideoProcessingService videoProcessing,
        ILogger<IndexModel> logger)
    {
        _environment = environment;
        _videoProcessing = videoProcessing;
        _logger = logger;
    }

    [BindProperty]
    public IFormFile? MeetingVideo { get; set; }

    public VideoProcessingResult? Result { get; private set; }

    public string? ErrorMessage { get; private set; }

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (MeetingVideo is null || MeetingVideo.Length == 0)
        {
            ModelState.AddModelError(nameof(MeetingVideo), "Please upload a meeting recording.");
            return Page();
        }

        var extension = Path.GetExtension(MeetingVideo.FileName).ToLowerInvariant();
        if (!AllowedExtensions.Contains(extension))
        {
            ModelState.AddModelError(nameof(MeetingVideo), "Unsupported video type. Upload MP4, MOV, MKV, AVI, or WEBM.");
            return Page();
        }

        if (MeetingVideo.Length > 2L * 1024 * 1024 * 1024)
        {
            ModelState.AddModelError(nameof(MeetingVideo), "Videos must be 2 GB or smaller.");
            return Page();
        }

        var uploadsRoot = Path.Combine(_environment.ContentRootPath, "App_Data", "uploads");
        Directory.CreateDirectory(uploadsRoot);
        var tempFile = Path.Combine(uploadsRoot, $"{Guid.NewGuid():N}{extension}");

        await using (var fileStream = System.IO.File.Create(tempFile))
        {
            await MeetingVideo.CopyToAsync(fileStream, cancellationToken);
        }

        try
        {
            Result = await _videoProcessing.ProcessAsync(tempFile, cancellationToken);
        }
        catch (VideoProcessingException ex)
        {
            ErrorMessage = ex.Message;
            _logger.LogWarning(ex, "Video processing failed with a known error.");
        }
        catch (Exception ex)
        {
            ErrorMessage = "We could not process the video. Check the server logs for details.";
            _logger.LogError(ex, "Unexpected processing failure.");
        }
        finally
        {
            System.IO.File.Delete(tempFile);
        }

        return Page();
    }
}
