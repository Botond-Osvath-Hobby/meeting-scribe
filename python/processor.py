#!/usr/bin/env python
import argparse
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
        default=os.getenv("WHISPER_MODEL_SIZE", "small"),
        help="Whisper model size (tiny, base, small, medium, large-v3, etc.).",
    )
    parser.add_argument(
        "--language",
        default="hu",
        help="Spoken language in ISO-639-1 format (default: 'hu' for Hungarian).",
    )
    parser.add_argument(
        "--summary-model",
        default=os.getenv("SUMMARY_MODEL", "google/flan-t5-base"),
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
    return audio_model.transcribe(str(video_path), language=language)


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
        do_sample=False,
        temperature=0.3,
    )
    return response[0]["generated_text"].strip()


def main() -> None:
    args = parse_args()
    video_path = Path(args.input)
    if not video_path.exists():
        raise FileNotFoundError(f"Video not found at {video_path}")

    transcript = load_transcript(video_path, args.model_size, args.language)
    segments = transcript.get("segments") or []
    notes = build_notes(segments)

    transcript_text = " ".join(segment.get("text", "").strip() for segment in segments).strip()
    if len(transcript_text) > args.max_transcript_chars:
        transcript_text = f"{transcript_text[: args.max_transcript_chars]}..."

    summary = summarize(transcript_text or transcript.get("text", ""), args.summary_model)

    payload = {
        "notes": notes,
        "businessSummary": summary,
    }

    print(json.dumps(payload, ensure_ascii=False))


if __name__ == "__main__":
    main()

