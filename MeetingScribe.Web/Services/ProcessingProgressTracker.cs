using System;
using System.Collections.Concurrent;

namespace MeetingScribe.Web.Services;

public static class ProgressStageKeys
{
    public const string Upload = "upload";
    public const string Transcribe = "transcribe";
    public const string Summarize = "summarize";
    public const string SummarizeGenerate = "summarize_generate";
    public const string Pipeline = "pipeline";
}

public static class ProgressStates
{
    public const string Pending = "pending";
    public const string Running = "running";
    public const string Completed = "completed";
    public const string Failed = "failed";
}

public class ProcessingProgressTracker
{
    private readonly ConcurrentDictionary<string, ProcessingProgress> _store = new();

    public void Start(string operationId)
    {
        if (string.IsNullOrWhiteSpace(operationId))
        {
            return;
        }

        _store[operationId] = new ProcessingProgress();
    }

    public ProcessingProgressSnapshot? GetSnapshot(string operationId)
    {
        if (!_store.TryGetValue(operationId, out var progress))
        {
            return null;
        }

        lock (progress)
        {
            return progress.ToSnapshot(operationId);
        }
    }

public void UpdateStage(string operationId, string stage, string state, string? message = null, double? percent = null)
    {
        if (string.IsNullOrWhiteSpace(operationId))
        {
            return;
        }

        Update(operationId, progress =>
        {
            var target = progress.GetStage(stage);
        target?.Update(state, message, percent);

            if (state == ProgressStates.Failed)
            {
                progress.UpdateOverall(ProgressStates.Failed, message);
            }
            else if (progress.IsCompleted)
            {
                progress.UpdateOverall(ProgressStates.Completed);
            }
            else if (state == ProgressStates.Running)
            {
                progress.UpdateOverall(ProgressStates.Running);
            }
        });
    }

    private void Update(string operationId, Action<ProcessingProgress> apply)
    {
        if (!_store.TryGetValue(operationId, out var progress))
        {
            return;
        }

        lock (progress)
        {
            apply(progress);
        }
    }
}

public class ProcessingProgress
{
    public ProgressStage Upload { get; } = new(ProgressStageKeys.Upload);
    public ProgressStage Transcribe { get; } = new(ProgressStageKeys.Transcribe);
    public ProgressStage Summarize { get; } = new(ProgressStageKeys.Summarize);
    public ProgressStage SummarizeGenerate { get; } = new(ProgressStageKeys.SummarizeGenerate);
    public string OverallState { get; private set; } = ProgressStates.Pending;
    public string? Message { get; private set; }
    public DateTimeOffset CreatedAt { get; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedAt { get; private set; }
    public bool IsCompleted =>
        Upload.State == ProgressStates.Completed &&
        Transcribe.State == ProgressStates.Completed &&
        Summarize.State == ProgressStates.Completed;

    public void UpdateOverall(string state, string? message = null)
    {
        OverallState = state;
        if (!string.IsNullOrWhiteSpace(message))
        {
            Message = message;
        }

        if ((state == ProgressStates.Completed || state == ProgressStates.Failed) && CompletedAt is null)
        {
            CompletedAt = DateTimeOffset.UtcNow;
        }
    }

    public ProgressStage? GetStage(string key)
    {
        return key switch
        {
            ProgressStageKeys.Upload => Upload,
            ProgressStageKeys.Transcribe => Transcribe,
            ProgressStageKeys.Summarize => Summarize,
            ProgressStageKeys.SummarizeGenerate => SummarizeGenerate,
            _ => null
        };
    }

    public ProcessingProgressSnapshot ToSnapshot(string operationId)
        => new()
        {
            OperationId = operationId,
            OverallState = OverallState,
            Message = Message,
            CreatedAt = CreatedAt,
            CompletedAt = CompletedAt,
            Upload = Upload.ToSnapshot(),
            Transcribe = Transcribe.ToSnapshot(),
            Summarize = Summarize.ToSnapshot(),
            SummarizeGenerate = SummarizeGenerate.ToSnapshot()
        };
}

public class ProgressStage
{
    public ProgressStage(string key)
    {
        Key = key;
    }

    public string Key { get; }
    public string State { get; private set; } = ProgressStates.Pending;
    public string? Message { get; private set; }
    public DateTimeOffset? StartedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public double? Percent { get; private set; }

    public void Update(string state, string? message = null, double? percent = null)
    {
        if (state == ProgressStates.Running && StartedAt is null)
        {
            StartedAt = DateTimeOffset.UtcNow;
        }

        if ((state == ProgressStates.Completed || state == ProgressStates.Failed) && CompletedAt is null)
        {
            CompletedAt = DateTimeOffset.UtcNow;
        }

        State = state;
        if (!string.IsNullOrWhiteSpace(message))
        {
            Message = message;
        }

        if (percent is not null)
        {
            Percent = Math.Clamp(percent.Value, 0, 1);
        }
    }

    public ProgressStageSnapshot ToSnapshot() => new()
    {
        Key = Key,
        State = State,
        Message = Message,
        StartedAt = StartedAt,
        CompletedAt = CompletedAt,
        Percent = Percent
    };
}

public class ProcessingProgressSnapshot
{
    public string OperationId { get; init; } = string.Empty;
    public string OverallState { get; init; } = ProgressStates.Pending;
    public string? Message { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? CompletedAt { get; init; }
    public ProgressStageSnapshot Upload { get; init; } = new();
    public ProgressStageSnapshot Transcribe { get; init; } = new();
    public ProgressStageSnapshot Summarize { get; init; } = new();
    public ProgressStageSnapshot SummarizeGenerate { get; init; } = new();
}

public class ProgressStageSnapshot
{
    public string Key { get; init; } = string.Empty;
    public string State { get; init; } = ProgressStates.Pending;
    public string? Message { get; init; }
    public DateTimeOffset? StartedAt { get; init; }
    public DateTimeOffset? CompletedAt { get; init; }
    public double? Percent { get; init; }
}

