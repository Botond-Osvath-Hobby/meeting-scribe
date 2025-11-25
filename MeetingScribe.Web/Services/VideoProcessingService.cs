using System.Diagnostics;
using System.Text;
using System.Text.Json;
using MeetingScribe.Web.Models;
using Microsoft.Extensions.Options;

namespace MeetingScribe.Web.Services;

public class VideoProcessingService
{
    private readonly IWebHostEnvironment _environment;
    private readonly VideoProcessingOptions _options;
    private readonly ILogger<VideoProcessingService> _logger;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public VideoProcessingService(
        IWebHostEnvironment environment,
        IOptions<VideoProcessingOptions> options,
        ILogger<VideoProcessingService> logger)
    {
        _environment = environment;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<VideoProcessingResult> ProcessAsync(
        string videoPath,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(videoPath))
        {
            throw new VideoProcessingException("The uploaded video could not be found on the server.");
        }

        var scriptPath = ResolvePath(_options.ScriptPath);
        if (!File.Exists(scriptPath))
        {
            throw new VideoProcessingException($"The Python script was not found at '{scriptPath}'.");
        }

        var pythonPath = string.IsNullOrWhiteSpace(_options.PythonExecutablePath)
            ? "python"
            : _options.PythonExecutablePath;

        var arguments = $"\"{scriptPath}\" --input \"{videoPath}\"";
        var startInfo = new ProcessStartInfo
        {
            FileName = pythonPath,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = _environment.ContentRootPath
        };

        using var process = new Process { StartInfo = startInfo };
        var stdOut = new StringBuilder();
        var stdErr = new StringBuilder();

        process.OutputDataReceived += (_, args) =>
        {
            if (args.Data is not null)
            {
                stdOut.AppendLine(args.Data);
            }
        };

        process.ErrorDataReceived += (_, args) =>
        {
            if (args.Data is not null)
            {
                stdErr.AppendLine(args.Data);
            }
        };

        try
        {
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            var timeout = _options.TimeoutSeconds > 0
                ? TimeSpan.FromSeconds(_options.TimeoutSeconds)
                : Timeout.InfiniteTimeSpan;

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            if (timeout != Timeout.InfiniteTimeSpan)
            {
                timeoutCts.CancelAfter(timeout);
            }

            try
            {
                await process.WaitForExitAsync(timeoutCts.Token);
            }
            catch (OperationCanceledException)
            {
                try
                {
                    process.Kill(true);
                }
                catch
                {
                    // ignored
                }

                throw new VideoProcessingException("Processing timed out. Try a shorter video or increase the timeout.");
            }
        }
        catch (Exception ex) when (ex is not VideoProcessingException)
        {
            throw new VideoProcessingException("The AI pipeline failed to start. Verify the Python runtime and dependencies.", ex);
        }

        if (process.ExitCode != 0)
        {
            _logger.LogError("Python script failed with code {Code}: {Error}", process.ExitCode, stdErr.ToString());
            throw new VideoProcessingException(
                "The AI pipeline returned an error. Check the application logs for the Python output.");
        }

        var payload = stdOut.ToString().Trim();
        if (string.IsNullOrWhiteSpace(payload))
        {
            throw new VideoProcessingException("No output was produced by the AI pipeline.");
        }

        try
        {
            var result = JsonSerializer.Deserialize<PythonResponse>(payload, _jsonOptions)
                ?? throw new VideoProcessingException("Unexpected AI response format.");

            if (string.IsNullOrWhiteSpace(result.BusinessSummary))
            {
                throw new VideoProcessingException("The AI response did not include a business summary.");
            }

            var notes = result.Notes?.Where(n => !string.IsNullOrWhiteSpace(n)).ToList()
                        ?? new List<string>();

            return new VideoProcessingResult(notes, result.BusinessSummary.Trim());
        }
        catch (VideoProcessingException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse Python output: {Payload}", payload);
            throw new VideoProcessingException("Could not parse the AI response. Ensure the script prints JSON.", ex);
        }
    }

    private string ResolvePath(string relativeOrAbsolutePath)
    {
        if (Path.IsPathRooted(relativeOrAbsolutePath))
        {
            return relativeOrAbsolutePath;
        }

        return Path.GetFullPath(Path.Combine(_environment.ContentRootPath, relativeOrAbsolutePath));
    }

    private sealed record PythonResponse(IReadOnlyList<string>? Notes, string? BusinessSummary);
}


