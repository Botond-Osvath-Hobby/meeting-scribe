# Meeting Scribe

Minimal login-free ASP.NET Core + Python helper that turns Hungarian meeting recordings into crisp, decision-focused notes.

## Prerequisites

- .NET 9 SDK
- Python 3.10+ with `pip`
- FFmpeg available on PATH (required by Whisper for audio extraction)
- GPU optional but strongly recommended for Whisper inference

## First-time setup

```bash
dotnet restore MeetingScribe.Web/MeetingScribe.Web.csproj
python -m venv .venv
.venv\Scripts\activate  # Windows
pip install -r python/requirements.txt
```

## Local run

```bash
dotnet run --project MeetingScribe.Web/MeetingScribe.Web.csproj
```

Open `https://localhost:5001` (or `http://localhost:5000`) and upload a Hungarian meeting video (MP4/MOV/MKV/AVI/WEBM, max 2 GB). The app:

1. Saves the file temporarily (no database needed).
2. Calls `python/processor.py` which:
   - Transcribes Hungarian speech with Whisper.
   - Builds timestamped notes.
   - Summarizes business decisions via a Transformers model.
3. Displays the raw notes and a decision-focused summary.

## Configuration

`appsettings.json` → `Processing` section:

- `PythonExecutablePath`: override if `python` isn’t on PATH.
- `ScriptPath`: relative or absolute path to `processor.py`.
- `TimeoutSeconds`: upper bound for Python processing.

Environment-specific overrides live in `appsettings.Development.json`.

## Customizing AI models

Environment variables consumed by `processor.py`:

- `WHISPER_MODEL_SIZE` (default `small`)
- `SUMMARY_MODEL` (default `google/flan-t5-base`)

You can also pass `--model-size` or `--summary-model` CLI flags when invoking the script manually.

## Production deployment notes

- Use persistent storage if you need to keep original uploads.
- Consider Azure App Service / AWS App Runner for the .NET app, and host Python dependencies on the same box (e.g., container image) to avoid cross-machine file copies.
- Add authentication + rate limiting before exposing publicly.
- Schedule cleanup of `App_Data/uploads` in case processing fails before deletion.

## Troubleshooting

- **“Python script was not found”** → ensure `python/processor.py` is deployed alongside the web project (included via linked content in the csproj).
- **“The AI pipeline failed to start”** → confirm the virtual environment is activated or set `Processing:PythonExecutablePath` to the full interpreter path.
- **Performance issues** → use `WHISPER_MODEL_SIZE=base` or `tiny` for faster but less accurate runs, or add GPU support (CUDA/cuDNN) for large models.
