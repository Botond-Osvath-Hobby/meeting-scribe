#!/usr/bin/env python
import argparse
import importlib
import json
import os
import sys
import textwrap
from pathlib import Path

try:
    import whisper  # type: ignore
    from transformers import pipeline  # type: ignore
except ImportError as exc:  # pragma: no cover - import guard
    sys.stderr.write(
        "Missing required Python packages. Run `pip install -r python/requirements.txt`.\n"
    )
    raise


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Transcribe a Hungarian meeting video and summarize business decisions."
    )
    parser.add_argument("--input", required=True, help="Path to the uploaded video file.")
    parser.add_argument(
        "--model-size",
        default=os.getenv("WHISPER_MODEL_SIZE", "large-v3"),
        help="Whisper model size (tiny, base, small, medium, large-v3, etc.).",
    )
    parser.add_argument(
        "--language",
        default="hu",
        help="Spoken language in ISO-639-1 format (default: 'hu' for Hungarian).",
    )
    parser.add_argument(
        "--summary-model",
        default=os.getenv("SUMMARY_MODEL", "SZTAKI-HLT/mT5-base-HunSum-2"),
        help="Transformers model used for abstractive summarization.",
    )
    parser.add_argument(
        "--max-transcript-chars",
        type=int,
        default=6000,
        help="Limit transcript length passed into the summarizer.",
    )
    return parser.parse_args()


def format_timestamp(seconds: float) -> str:
    seconds = max(0, int(seconds))
    hours, remainder = divmod(seconds, 3600)
    minutes, secs = divmod(remainder, 60)
    return f"{hours:02d}:{minutes:02d}:{secs:02d}"


def build_notes(segments) -> list[str]:
    notes: list[str] = []
    for segment in segments:
        text = (segment.get("text") or "").strip()
        if not text:
            continue
        start = format_timestamp(segment.get("start", 0))
        end = format_timestamp(segment.get("end", 0))
        notes.append(f"{start} - {end} · {text}")
    return notes


def load_transcript(video_path: Path, model_size: str, language: str):
    audio_model = whisper.load_model(model_size)
    whisper_transcribe = importlib.import_module("whisper.transcribe")
    tqdm_module = getattr(whisper_transcribe, "tqdm", None)
    if tqdm_module is None or not hasattr(tqdm_module, "tqdm"):
        return audio_model.transcribe(
            str(video_path),
            language=language,
            verbose=False,
        )

    original_tqdm = tqdm_module.tqdm

    def make_progress_bar(*args, **kwargs):
        bar = original_tqdm(*args, **kwargs)
        original_update = bar.update

        def update(n=1):
            original_update(n)
            total = bar.total or 0
            if total <= 0:
                return
            percent = max(0.0, min(1.0, bar.n / total))
            report(
                "transcribe",
                "running",
                json.dumps(
                    {
                        "percent": percent,
                        "message": f"Whisper {percent * 100:.1f}% done",
                    },
                    ensure_ascii=False,
                ),
            )

        bar.update = update
        return bar

    tqdm_module.tqdm = make_progress_bar

    try:
        return audio_model.transcribe(
            str(video_path),
            language=language,
            verbose=False,
        )
    finally:
        tqdm_module.tqdm = original_tqdm


def summarize(transcript_text: str, model_name: str) -> str:
    summarizer = pipeline("text2text-generation", model=model_name)
    prompt = textwrap.dedent(
        f"""
        Te egy magyar üzleti meeting jegyzőkönyv specialista vagy.
        Az alábbi nyers, időrendi jegyzetekből készíts tömör, de részletes összefoglalót,
        amely felsorolja a kulcs témákat, az összes felmerült üzleti opciót és a végső döntéseket.
        Indokold a döntéseket a beszélgetés alapján, és emeld ki, ha valami follow-upot igényel.

        Jegyzetek:
        {transcript_text}
        """
    ).strip()
    response = summarizer(
        prompt,
        max_length=512,
        min_length=120,
    )
    return response[0]["generated_text"].strip()


PROGRESS_PREFIX = "__PROGRESS__"


def report(stage: str, state: str, message: str = "") -> None:
    print(f"{PROGRESS_PREFIX}|{stage}|{state}|{message}", flush=True)


def main() -> None:
    args = parse_args()
    video_path = Path(args.input)
    if not video_path.exists():
        raise FileNotFoundError(f"Video not found at {video_path}")

    report("transcribe", "running", json.dumps({"message": "Starting Whisper"}, ensure_ascii=False))
    transcript = load_transcript(video_path, args.model_size, args.language)
    segments = transcript.get("segments") or []
    notes = build_notes(segments)
    report(
        "transcribe",
        "completed",
        json.dumps(
            {
                "percent": 1.0,
                "message": f"{len(notes)} timestamped notes ready",
            },
            ensure_ascii=False,
        ),
    )

    transcript_text = " ".join(segment.get("text", "").strip() for segment in segments).strip()
    if len(transcript_text) > args.max_transcript_chars:
        transcript_text = f"{transcript_text[: args.max_transcript_chars]}..."

    report("summarize", "running", json.dumps({"message": "Summarizing decisions"}, ensure_ascii=False))
    summary = summarize(transcript_text or transcript.get("text", ""), args.summary_model)
    report("summarize", "completed", json.dumps({"message": "Summary ready"}, ensure_ascii=False))

    payload = {
        "notes": notes,
        "businessSummary": summary,
    }

    print(json.dumps(payload, ensure_ascii=False))


if __name__ == "__main__":
    try:
        main()
    except Exception as exc:  # pragma: no cover
        report("pipeline", "failed", str(exc))
        raise

