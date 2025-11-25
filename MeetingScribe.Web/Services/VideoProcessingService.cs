using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Threading;
using MeetingScribe.Web.Models;
using Microsoft.Extensions.Options;

namespace MeetingScribe.Web.Services;

public class VideoProcessingService
{
    private const string ProgressPrefix = "__PROGRESS__|";
    private static readonly HashSet<string> VideoExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".mp4", ".mov", ".mkv", ".avi", ".webm" };
    private static readonly HashSet<string> AudioExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".mp3", ".wav", ".m4a", ".aac", ".flac", ".ogg" };
    private readonly IWebHostEnvironment _environment;
    private readonly VideoProcessingOptions _options;
    private readonly ILogger<VideoProcessingService> _logger;
    private readonly ProcessingProgressTracker _progressTracker;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public VideoProcessingService(
        IWebHostEnvironment environment,
        IOptions<VideoProcessingOptions> options,
        ILogger<VideoProcessingService> logger,
        ProcessingProgressTracker progressTracker)
    {
        _environment = environment;
        _options = options.Value;
        _logger = logger;
        _progressTracker = progressTracker;
    }

    public async Task<VideoProcessingResult> ProcessAsync(
        string videoPath,
        string? operationId,
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

        var mediaPath = videoPath;
        var tempFiles = new List<string>();
        var cleanupPaths = new List<string>();

        try
        {
            try
            {
                mediaPath = await PrepareMediaAsync(videoPath, tempFiles, cancellationToken);
            }
            catch (VideoProcessingException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new VideoProcessingException("Failed to prepare the uploaded file for processing.", ex);
            }

            var arguments = $"\"{scriptPath}\" --input \"{mediaPath}\"";
        if (!string.IsNullOrWhiteSpace(_options.WhisperModelSize))
        {
            arguments += $" --model-size \"{_options.WhisperModelSize}\"";
        }
        if (!string.IsNullOrWhiteSpace(_options.SummaryModel))
        {
            arguments += $" --summary-model \"{_options.SummaryModel}\"";
        }

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
            startInfo.Environment["PYTHONIOENCODING"] = "utf-8";

            using var process = new Process { StartInfo = startInfo };
            var stdOut = new StringBuilder();
            var stdErr = new StringBuilder();

        process.OutputDataReceived += (_, args) =>
        {
            if (args.Data is null)
            {
                return;
            }

            if (TryHandleProgressSignal(operationId, args.Data))
            {
                return;
            }

            lock (stdOut)
            {
                stdOut.AppendLine(args.Data);
            }

            _logger.LogInformation("PY> {Line}", args.Data);
        };

        process.ErrorDataReceived += (_, args) =>
        {
            if (args.Data is not null)
            {
                stdErr.AppendLine(args.Data);
                _logger.LogInformation("PROCESSOR> {Line}", args.Data);
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
                    TerminateProcess(process);
                    _progressTracker.UpdateStage(operationId ?? string.Empty, ProgressStageKeys.Transcribe, ProgressStates.Failed, "Processing timed out.");
                    throw new VideoProcessingException("Processing timed out. Try a shorter video or increase the timeout.");
                }
            }
            catch (Exception ex) when (ex is not VideoProcessingException)
            {
                TerminateProcess(process);
                _progressTracker.UpdateStage(operationId ?? string.Empty, ProgressStageKeys.Transcribe, ProgressStates.Failed, "Failed to start AI pipeline.");
                throw new VideoProcessingException("The AI pipeline failed to start. Verify the Python runtime and dependencies.", ex);
            }

            if (process.ExitCode != 0)
            {
                _logger.LogError("Python script failed with code {Code}: {Error}", process.ExitCode, stdErr.ToString());
                _progressTracker.UpdateStage(operationId ?? string.Empty, ProgressStageKeys.Transcribe, ProgressStates.Failed, "Python pipeline failed.");
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

                var finalResult = new VideoProcessingResult(notes, result.BusinessSummary.Trim());
                _progressTracker.UpdateStage(operationId ?? string.Empty, ProgressStageKeys.Summarize, ProgressStates.Completed, "Summary ready.");
                return finalResult;
            }
            catch (VideoProcessingException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to parse Python output: {Payload}", payload);
                _progressTracker.UpdateStage(operationId ?? string.Empty, ProgressStageKeys.Summarize, ProgressStates.Failed, "Could not parse AI response.");
                throw new VideoProcessingException("Could not parse the AI response. Ensure the script prints JSON.", ex);
            }
        }
        finally
        {
            _logger.LogInformation("Cleaning up media artifacts for operation {OperationId}", operationId ?? "<unknown>");
            foreach (var path in cleanupPaths)
            {
                _logger.LogDebug("Deleting cleanup artifact {Path}", path);
                TryDelete(path);
            }

            foreach (var tempFile in tempFiles)
            {
                _logger.LogDebug("Deleting temp file {Path}", tempFile);
                TryDelete(tempFile);
            }

            if (!string.Equals(mediaPath, videoPath, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogDebug("Deleting media path {Path}", mediaPath);
                TryDelete(mediaPath);
            }

            _logger.LogInformation("Cleanup routine completed for operation {OperationId}", operationId ?? "<unknown>");
        }
    }

    private async Task<string> PrepareMediaAsync(string originalPath, List<string> tempFiles, CancellationToken cancellationToken)
    {
        var extension = Path.GetExtension(originalPath).ToLowerInvariant();
        var requiresVideoExtract = VideoExtensions.Contains(extension);
        var requiresAudioTranscode = AudioExtensions.Contains(extension) && !string.Equals(extension, ".wav", StringComparison.OrdinalIgnoreCase);

        if (!requiresVideoExtract && !requiresAudioTranscode)
        {
            return originalPath;
        }

        var ffmpegPath = string.IsNullOrWhiteSpace(_options.FfmpegPath) ? "ffmpeg" : _options.FfmpegPath;
        var audioPath = Path.Combine(Path.GetDirectoryName(originalPath) ?? Path.GetTempPath(), $"{Guid.NewGuid():N}.wav");
        await RunFfmpegAsync(ffmpegPath, originalPath, audioPath, cancellationToken);
        tempFiles.Add(audioPath);
        _logger.LogInformation("Generated temporary audio {AudioPath} from {Source}", audioPath, originalPath);
        return audioPath;
    }

    private static async Task RunFfmpegAsync(string ffmpegPath, string inputPath, string outputPath, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = ffmpegPath,
            Arguments = $"-y -i \"{inputPath}\" -vn -acodec pcm_s16le -ar 16000 -ac 1 \"{outputPath}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = new Process { StartInfo = startInfo };
        var stderr = new StringBuilder();
        process.ErrorDataReceived += (_, args) =>
        {
            if (args.Data is not null)
            {
                stderr.AppendLine(args.Data);
            }
        };

        try
        {
            process.Start();
            process.BeginErrorReadLine();
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(true);
                }
            }
            catch
            {
                // ignored
            }

            throw new VideoProcessingException("ffmpeg failed to convert the media file.", ex);
        }

        if (process.ExitCode != 0 || !File.Exists(outputPath))
        {
            throw new VideoProcessingException($"ffmpeg exited with code {process.ExitCode}. {stderr}");
        }
    }

    private static void TerminateProcess(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(true);
                process.WaitForExit(5000);
            }
        }
        catch
        {
            // ignored
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

    private void TryDelete(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
                return;
            }
            catch (IOException) when (attempt < 4)
            {
                Thread.Sleep(250);
            }
            catch (UnauthorizedAccessException) when (attempt < 4)
            {
                Thread.Sleep(250);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to delete temporary file {Path}", path);
                return;
            }
        }

        if (File.Exists(path))
        {
            _logger.LogWarning("Temporary file {Path} could not be deleted after multiple attempts.", path);
        }
    }

    private bool TryHandleProgressSignal(string? operationId, string? line)
    {
        if (string.IsNullOrWhiteSpace(operationId)
            || string.IsNullOrWhiteSpace(line)
            || !line.StartsWith(ProgressPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        var payload = line[ProgressPrefix.Length..];
        var parts = payload.Split('|', 3, StringSplitOptions.TrimEntries);
        if (parts.Length < 2)
        {
            return false;
        }

        var stage = parts[0];
        var state = parts[1];
        var message = parts.Length >= 3 ? parts[2] : null;
        double? percent = null;

        if (!string.IsNullOrWhiteSpace(message) && message.TrimStart().StartsWith("{"))
        {
            try
            {
                using var json = JsonDocument.Parse(message);
                if (json.RootElement.TryGetProperty("percent", out var percentElement) &&
                    percentElement.TryGetDouble(out var parsedPercent))
                {
                    percent = parsedPercent;
                }

                if (json.RootElement.TryGetProperty("message", out var messageElement))
                {
                    message = messageElement.GetString();
                }
            }
            catch
            {
                // ignore parsing errors; fallback to raw message string.
            }
        }

        _progressTracker.UpdateStage(operationId, stage, state, message, percent);
        return true;
    }

    private sealed record PythonResponse(IReadOnlyList<string>? Notes, string? BusinessSummary);
}


