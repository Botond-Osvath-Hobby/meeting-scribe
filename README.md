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

Open `https://localhost:5001` (or `http://localhost:5000`) and upload a Hungarian meeting recording (video: MP4/MOV/MKV/AVI/WEBM or audio: MP3/WAV/M4A/FLAC/OGG, max 2 GB). The app:

1. Saves the file temporarily (no database needed) and streams live stage updates to the UI (upload → transcription → summary).
2. Runs `python/processor.py`, which:
   - Transcribes Hungarian speech with Whisper.
   - Builds timestamped notes.
   - Summarizes business decisions via a Transformers model.
3. Displays the raw notes and a decision-focused summary without leaving the page.

## Configuration

`appsettings.json` → `Processing` section:

- `PythonExecutablePath`: override if `python` isn’t on PATH.
- `ScriptPath`: relative or absolute path to `processor.py`.
- `TimeoutSeconds`: upper bound for Python processing (defaults to 7 200 s ≈ 2 h).
- `WhisperModelSize`: passed to the script’s `--model-size` flag (default `large-v3` for stronger Hungarian recognition).
- `SummaryModel`: passed to the script’s `--summary-model` flag (default `Szumis/HuBERT-XL-captions` tuned for HU business notes).
- `FfmpegPath`: command used when extracting audio from uploaded videos (`ffmpeg` by default).

Environment-specific overrides live in `appsettings.Development.json`.

## Customizing AI models

Environment variables consumed by `processor.py`:

- `WHISPER_MODEL_SIZE` (default `large-v3`)
- `SUMMARY_MODEL` (default `Szumis/HuBERT-XL-captions`)

You can also pass `--model-size` or `--summary-model` CLI flags when invoking the script manually. The web app already passes the values from `Processing:WhisperModelSize` and `Processing:SummaryModel`.

## Production deployment notes

- Use persistent storage if you need to keep original uploads.
- Consider Azure App Service / AWS App Runner for the .NET app, and host Python dependencies on the same box (e.g., container image) to avoid cross-machine file copies.
- Add authentication + rate limiting before exposing publicly.
- Schedule cleanup of `App_Data/uploads` in case processing fails before deletion.

## Troubleshooting

- **“Python script was not found”** → ensure `python/processor.py` is deployed alongside the web project (included via linked content in the csproj).
- **“The AI pipeline failed to start”** → confirm the virtual environment is activated or set `Processing:PythonExecutablePath` to the full interpreter path.
- **Performance issues** → use `WHISPER_MODEL_SIZE=base` or `tiny` for faster but less accurate runs, or add GPU support (CUDA/cuDNN) for large models.
