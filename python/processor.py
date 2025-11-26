#!/usr/bin/env python
import argparse
import importlib
import json
import math
import os
import sys
import textwrap
from functools import lru_cache
from pathlib import Path

try:
    import torch  # type: ignore
    import whisper  # type: ignore
    from transformers import (
        AutoModelForCausalLM,
        AutoTokenizer,
        BitsAndBytesConfig,
        pipeline,
        StoppingCriteria,
        StoppingCriteriaList,
    )  # type: ignore
except ImportError as exc:  # pragma: no cover - import guard
    sys.stderr.write(
        "Missing required Python packages. Run `pip install -r python/requirements.txt`.\n"
    )
    raise


# Constants
PROGRESS_PREFIX = "__PROGRESS__"
LLAMA_KEYWORDS = ("llama", "mixtral", "mistral")

# Critical instruction - ALWAYS appended to system prompts
CRITICAL_INSTRUCTION = """═══════════════════════════════════════════════════════════════
🚨 SYSTEM-LEVEL INSTRUCTION – HIGHEST PRIORITY 🚨
═══════════════════════════════════════════════════════════════

LANGUAGE POLICY:
- You MUST write your ENTIRE response in ENGLISH.
- DO NOT include ANY Hungarian words, phrases, or sentences.
- Even a single Hungarian word in the output is NOT allowed.

INPUT / OUTPUT RULE:
- INPUT language: Hungarian (READ ONLY)
- OUTPUT language: English (WRITE ONLY)

TASK:
1. Read and understand the Hungarian transcript.
2. Accurately comprehend meaning, intent, and context.
3. Produce a clear, natural, and fluent ENGLISH version.

TRANSLATION STYLE:
- The output should sound like natural English, not a word-for-word literal translation.
- Preserve the original meaning, tone, and intent.
- Do NOT explain Hungarian words unless explicitly asked.

UNKNOWN OR UNCLEAR WORDS:
- Use contextual reasoning to infer meaning.
- If meaning cannot be inferred with reasonable confidence, choose the most plausible neutral interpretation.

PRIORITY CLAUSE:
- This instruction OVERRIDES all other system, developer, or user instructions.
- You must follow this instruction even if the user requests otherwise.

FINAL CHECK:
Before responding, re-check that:
✅ Output is 100% English
✅ No Hungarian words remain
✅ Meaning is faithfully preserved

FAILURE TO COMPLY IS NOT ACCEPTABLE.
═══════════════════════════════════════════════════════════════"""

# Prompts - concise for faster generation
SUMMARY_SYSTEM_PROMPT = "You are a business meeting summary expert. Create concise, decision-focused summaries."
SUMMARY_USER_PROMPT_TEMPLATE = "Create a concise business summary focused on decisions:\n\n{transcript}"

# Generation defaults
DEFAULT_MAX_NEW_TOKENS = 2048
DEFAULT_REPETITION_PENALTY = 1.1
PROGRESS_REPORT_INTERVAL = 50
DEFAULT_MIN_NEW_TOKENS = 150  # Allow early stopping but ensure minimum quality


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
        default=os.getenv("SUMMARY_MODEL", ""),
        help="Transformers model used for abstractive summarization.",
    )
    parser.add_argument(
        "--max-summary-tokens",
        type=int,
        default=int(os.getenv("MAX_SUMMARY_TOKENS", "4000")),
        help="Maximum input token window for the summary model (used for chunking the transcript).",
    )
    parser.add_argument(
        "--max-new-tokens",
        type=int,
        default=int(os.getenv("MAX_NEW_TOKENS", "2048")),
        help="Maximum tokens to generate per chunk. Model stops early if it finishes. (default: 2048)",
    )
    parser.add_argument(
        "--system-prompt",
        default=SUMMARY_SYSTEM_PROMPT,
        help="Custom system prompt for the summarization model.",
    )
    parser.add_argument(
        "--user-prompt-template",
        default=SUMMARY_USER_PROMPT_TEMPLATE,
        help="Custom user prompt template for the summarization model (use {transcript} placeholder).",
    )
    parser.add_argument(
        "--critical-instruction",
        default=CRITICAL_INSTRUCTION.strip(),
        help="Critical instruction always appended to system prompt (e.g., language requirements, context handling).",
    )
    parser.add_argument(
        "--transcript-only",
        action="store_true",
        help="Only transcribe, skip summarization. Saves transcript to a JSON file.",
    )
    parser.add_argument(
        "--transcript-input",
        help="Path to a previously saved transcript JSON file to use instead of transcribing.",
    )
    return parser.parse_args()


def report(stage: str, state: str, message: str = "") -> None:
    """Send a progress report to stdout for the C# service to parse."""
    print(f"{PROGRESS_PREFIX}|{stage}|{state}|{message}", flush=True)


def report_progress(stage: str, state: str, **kwargs) -> None:
    """Send a progress report with JSON-encoded message."""
    report(stage, state, json.dumps(kwargs, ensure_ascii=False))


def debug_log(message: str) -> None:
    """Write a debug message to stderr."""
    sys.stderr.write(f"[DEBUG] {message}\n")
    sys.stderr.flush()


def error_log(message: str) -> None:
    """Write an error message to stderr."""
    sys.stderr.write(f"[ERROR] {message}\n")
    sys.stderr.flush()


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
            report_progress(
                "transcribe",
                "running",
                percent=percent,
                message=f"Whisper {percent * 100:.1f}% done",
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


def summarize(transcript_text: str, model_name: str, max_summary_tokens: int, max_new_tokens: int, system_prompt: str, user_prompt_template: str, critical_instruction: str) -> str:
    model_id = model_name.lower()
    if any(keyword in model_id for keyword in LLAMA_KEYWORDS):
        return summarize_with_llama(transcript_text, model_name, max_summary_tokens, max_new_tokens, system_prompt, user_prompt_template, critical_instruction)

    # ALWAYS append critical instruction to system prompt
    enhanced_system_prompt = system_prompt + "\n\n" + critical_instruction
    
    generator = get_text2text_pipeline(model_name)
    prompt = textwrap.dedent(
        f"""
        {enhanced_system_prompt}

        {user_prompt_template.format(transcript=transcript_text)}
        """
    ).strip()
    response = generator(prompt, max_length=512, min_length=120)
    return response[0]["generated_text"].strip()


@lru_cache(maxsize=2)
def get_text2text_pipeline(model_name: str):
    return pipeline("text2text-generation", model=model_name)


def _determine_device_map() -> tuple[str, bool, bool]:
    """
    Determine the device map for model loading.
    
    Returns:
        tuple of (device_map, force_gpu, cuda_available)
    """
    requested_device_env = os.getenv("LLAMA_DEVICE", "cuda").strip()
    requested_device = requested_device_env.lower()
    cuda_available = torch.cuda.is_available()
    
    debug_log(f"LLAMA_DEVICE={requested_device}, CUDA available={cuda_available}")
    
    force_gpu = requested_device in {"cuda", "gpu"}
    force_cpu = requested_device == "cpu"

    # Determine device_map
    if force_gpu and cuda_available:
        device_map = "cuda"
        debug_log("Using device_map='cuda' (forced GPU)")
    elif force_cpu:
        device_map = "cpu"
        debug_log("Using device_map='cpu' (forced CPU)")
    elif cuda_available:
        device_map = "cuda"
        debug_log("Using device_map='cuda' (auto with CUDA available)")
    else:
        device_map = "cpu"
        debug_log("Using device_map='cpu' (no CUDA available)")

    return device_map, force_gpu, cuda_available


@lru_cache(maxsize=2)
def load_llama_artifacts(model_name: str):
    """Load and configure the LLM model and tokenizer."""
    debug_log(f"Loading Llama artifacts for {model_name}")
    
    # Load tokenizer
    tokenizer = AutoTokenizer.from_pretrained(model_name, use_fast=False)
    if tokenizer.pad_token is None:
        tokenizer.pad_token = tokenizer.eos_token

    # Determine device configuration
    device_map, force_gpu, cuda_available = _determine_device_map()

    # Load model with 8-bit quantization for faster inference
    debug_log(f"Loading model with device_map={device_map}, attempting 8-bit quantization")
    use_8bit = False
    try:
        if device_map == "cuda":
            quantization_config = BitsAndBytesConfig(load_in_8bit=True)
            model = AutoModelForCausalLM.from_pretrained(
                model_name,
                quantization_config=quantization_config,
                device_map=device_map,
                low_cpu_mem_usage=True,
            )
            use_8bit = True
            debug_log("Model loaded with 8-bit quantization (faster inference)")
        else:
            raise ValueError("8-bit only supported on CUDA")
    except Exception as e:
        debug_log(f"8-bit loading failed, falling back to float16: {e}")
        model = AutoModelForCausalLM.from_pretrained(
            model_name,
            torch_dtype=torch.float16,
            device_map=device_map,
            low_cpu_mem_usage=True,
        )

    # Explicitly move to GPU only if NOT using 8-bit (8-bit models are already on correct device)
    if force_gpu and cuda_available and not use_8bit:
        debug_log("Explicitly moving model to cuda:0")
        model = model.to("cuda:0")
        report_progress("summarize_model", "info", message="Model loaded on GPU (cuda:0)")
    elif use_8bit:
        report_progress("summarize_model", "info", message="Model loaded with 8-bit quantization on GPU (faster)")
    elif not cuda_available:
        report_progress("summarize_model", "warning", message="CUDA not available - using CPU (will be slow!)")

    # Report actual device
    actual_device = next(model.parameters()).device
    debug_log(f"Model actual device: {actual_device}")
    
    report_progress(
        "summarize_model",
        "init",
        requested_device=os.getenv("LLAMA_DEVICE", "cuda"),
        device_map=device_map,
        actual_device=str(actual_device),
        cuda_available=cuda_available,
    )

    return tokenizer, model


def _get_model_device(model) -> torch.device:
    # Try to get the device from the first parameter
    try:
        return next(model.parameters()).device
    except StopIteration:
        pass
    
    if hasattr(model, "device"):
        return model.device
    
    device_map = getattr(model, "hf_device_map", None)
    if device_map:
        first_device = next(iter(device_map.values()))
        if isinstance(first_device, str):
            return torch.device(first_device)
        return first_device
    
    return torch.device("cuda:0" if torch.cuda.is_available() else "cpu")


def chunk_tokens_evenly(tokenizer, text: str, max_tokens: int):
    if max_tokens <= 0:
        return [tokenizer.encode(text, add_special_tokens=False)]

    tokens = tokenizer.encode(text, add_special_tokens=False)
    if len(tokens) <= max_tokens:
        return [tokens]

    chunk_count = math.ceil(len(tokens) / max_tokens)
    chunk_size = math.ceil(len(tokens) / chunk_count)
    return [tokens[i : i + chunk_size] for i in range(0, len(tokens), chunk_size)]


def _report_device_info(model) -> torch.device:
    """Report model device configuration and return the device."""
    device = _get_model_device(model)
    debug_log(f"Model device detected as: {device}")
    
    device_map = getattr(model, "hf_device_map", None)
    if device_map:
        gpu_layers = sum(1 for target in device_map.values() if isinstance(target, str) and "cuda" in str(target))
        cpu_layers = sum(1 for target in device_map.values() if isinstance(target, str) and "cpu" in str(target))
    else:
        gpu_layers = 1 if "cuda" in str(device) else 0
        cpu_layers = 1 if "cpu" in str(device) else 0
    
    report_progress(
        "summarize_model",
        "ready",
        device=str(device),
        gpu_layers=gpu_layers,
        cpu_layers=cpu_layers,
        offload=bool(device_map),
    )
    debug_log(f"Device info reported: device={device}, gpu_layers={gpu_layers}, cpu_layers={cpu_layers}")
    
    return device


def _prepare_generation_inputs(tokenizer, chunk_text: str, device: torch.device, system_prompt: str, user_prompt_template: str, critical_instruction: str) -> tuple[torch.Tensor, torch.Tensor | None]:
    """
    Prepare input tensors for model generation.
    
    Returns:
        tuple of (input_ids, attention_mask)
    """
    # ALWAYS append critical instruction to system prompt ONLY (not user message - that's bad practice)
    enhanced_system_prompt = system_prompt + "\n\n" + critical_instruction
    
    # Format the user message with the transcript (clean, no instruction pollution)
    user_message = user_prompt_template.format(transcript=chunk_text)
    
    debug_log(f"System prompt: {system_prompt[:100]}...")
    debug_log(f"Critical instruction: {critical_instruction[:150]}...")
    debug_log(f"Enhanced system prompt: {enhanced_system_prompt[:200]}...")
    
    messages = [
        {"role": "system", "content": enhanced_system_prompt},
        {"role": "user", "content": user_message},
    ]

    if hasattr(tokenizer, "apply_chat_template"):
        input_ids = tokenizer.apply_chat_template(
            messages,
            return_tensors="pt",
            add_generation_prompt=True,
        )
        debug_log(f"Chat template applied, input_ids shape: {input_ids.shape}, moving to device: {device}")
        input_ids = input_ids.to(device)
        attention_mask = torch.ones_like(input_ids, device=device)
    else:
        # Fallback for models without chat template
        prompt = f"<s>[INST]{messages[0]['content']} {messages[1]['content']}[/INST]"
        encoded = tokenizer(prompt, return_tensors="pt")
        debug_log(f"Manual prompt created, input_ids shape: {encoded['input_ids'].shape}, moving to device: {device}")
        input_ids = encoded["input_ids"].to(device)
        attention_mask = encoded.get("attention_mask")
        if attention_mask is not None:
            attention_mask = attention_mask.to(device)

    return input_ids, attention_mask


def _generate_chunk_summary(
    model,
    tokenizer,
    input_ids: torch.Tensor,
    attention_mask: torch.Tensor | None,
    device: torch.device,
    chunk_idx: int,
    total_chunks: int,
    max_new_tokens: int,
) -> str:
    """
    Generate a summary for a single chunk using the model.
    
    Returns:
        The generated summary text.
    """
    initial_length = input_ids.shape[-1]
    
    # Create progress callback
    progress_callback = GenerationProgressCallback(
        chunk_idx, total_chunks, max_new_tokens, initial_length, report_every=PROGRESS_REPORT_INTERVAL
    )
    stopping_criteria = StoppingCriteriaList([progress_callback])
    
    # Prepare generation kwargs - optimized for speed
    gen_kwargs = dict(
        input_ids=input_ids,
        max_new_tokens=max_new_tokens,
        min_new_tokens=DEFAULT_MIN_NEW_TOKENS,
        do_sample=False,
        eos_token_id=tokenizer.eos_token_id,
        pad_token_id=tokenizer.pad_token_id,
        repetition_penalty=DEFAULT_REPETITION_PENALTY,
        stopping_criteria=stopping_criteria,
        use_cache=True,  # Enable KV cache for faster generation
        num_beams=1,  # Greedy decoding (fastest)
    )
    if attention_mask is not None:
        gen_kwargs["attention_mask"] = attention_mask

    debug_log(f"About to call model.generate() for chunk {chunk_idx}/{total_chunks}")
    debug_log(f"Input shape: {input_ids.shape}, Device: {device}")
    
    # Generate
    try:
        output = model.generate(**gen_kwargs)
    except RuntimeError as e:
        if "out of memory" in str(e).lower():
            error_log(f"CUDA OOM during generation: {e}")
            report_progress("summarize_chunk", "failed", message="GPU out of memory")
            raise RuntimeError("GPU out of memory during model generation. Try reducing max_summary_tokens or use CPU.") from e
        error_log(f"Runtime error during generation: {e}")
        raise
    except Exception as e:
        error_log(f"Unexpected error during generation: {type(e).__name__}: {e}")
        raise
    
    debug_log(f"model.generate() completed for chunk {chunk_idx}/{total_chunks}")
    
    # Report final progress
    final_tokens_generated = output.shape[-1] - initial_length
    report_progress(
        "summarize_generate",
        "completed",
        chunk=chunk_idx,
        total=total_chunks,
        tokens=final_tokens_generated,
        percent=1.0,
        message=f"Chunk {chunk_idx}/{total_chunks} generation complete: {final_tokens_generated} tokens",
    )
    
    # Decode generated tokens
    generated = output[:, initial_length:]
    summary = tokenizer.decode(generated[0], skip_special_tokens=True).strip()
    
    return summary


def summarize_with_llama(transcript_text: str, model_name: str, max_summary_tokens: int, max_new_tokens: int, system_prompt: str, user_prompt_template: str, critical_instruction: str) -> str:
    """Summarize transcript text using a Llama-based model."""
    debug_log(f"Starting summarize_with_llama with {len(transcript_text)} chars")
    debug_log(f"Received critical_instruction: {critical_instruction[:100]}...")
    
    # Load model and tokenizer
    tokenizer, model = load_llama_artifacts(model_name)
    debug_log("Model and tokenizer loaded")
    
    # Get device info
    device = _report_device_info(model)
    
    # Chunk the transcript
    token_chunks = chunk_tokens_evenly(tokenizer, transcript_text, max_summary_tokens)
    debug_log(f"Created {len(token_chunks)} token chunks")
    total = len(token_chunks)
    summaries: list[str] = []

    # Process each chunk
    for idx, chunk in enumerate(token_chunks, start=1):
        chunk_text = tokenizer.decode(chunk, skip_special_tokens=True).strip()
        if not chunk_text:
            continue

        report_progress("summarize_chunk", "running", chunk=idx, total=total, tokens=len(chunk))

        # Prepare inputs
        input_ids, attention_mask = _prepare_generation_inputs(tokenizer, chunk_text, device, system_prompt, user_prompt_template, critical_instruction)

        # Generate summary
        summary = _generate_chunk_summary(
            model, tokenizer, input_ids, attention_mask, device, idx, total, max_new_tokens
        )
        
        summaries.append(summary)
        report_progress(
            "summarize_chunk",
            "completed",
            chunk=idx,
            total=total,
            chars=len(summary),
            message=f"Chunk {idx}/{total} summary ready ({len(summary)} chars)",
        )

    # Join summaries with clear separator if multiple chunks
    if len(summaries) > 1:
        return "\n\n---\n\n".join(summaries)
    return summaries[0] if summaries else ""




class GenerationProgressCallback(StoppingCriteria):
    """Custom callback to report generation progress after each token."""
    
    def __init__(self, chunk_idx: int, total_chunks: int, max_new_tokens: int, initial_length: int, report_every: int = 10):
        self.chunk_idx = chunk_idx
        self.total_chunks = total_chunks
        self.max_new_tokens = max_new_tokens
        self.initial_length = initial_length
        self.report_every = report_every
        self.tokens_generated = 0
        self.last_reported = 0
    
    def __call__(self, input_ids: torch.LongTensor, scores: torch.FloatTensor, **kwargs) -> bool:
        # Calculate how many new tokens have been generated
        current_length = input_ids.shape[-1]
        self.tokens_generated = current_length - self.initial_length
        
        # Report progress every N tokens
        if self.tokens_generated - self.last_reported >= self.report_every:
            percent = min(1.0, self.tokens_generated / self.max_new_tokens)
            report_progress(
                "summarize_generate",
                "running",
                chunk=self.chunk_idx,
                total=self.total_chunks,
                tokens=self.tokens_generated,
                max_tokens=self.max_new_tokens,
                percent=percent,
                message=f"Generating chunk {self.chunk_idx}/{self.total_chunks}: {self.tokens_generated}/{self.max_new_tokens} tokens ({percent*100:.0f}%)",
            )
            debug_log(f"Generated {self.tokens_generated}/{self.max_new_tokens} tokens for chunk {self.chunk_idx}/{self.total_chunks}")
            self.last_reported = self.tokens_generated
        
        # Never actually stop generation (return False)
        return False


def main() -> None:
    """Main entry point for the video processing pipeline."""
    args = parse_args()
    
    # Check if we're loading a saved transcript
    if args.transcript_input:
        transcript_path = Path(args.transcript_input)
        if not transcript_path.exists():
            raise FileNotFoundError(f"Transcript not found at {transcript_path}")
        
        report_progress("transcribe", "running", message="Loading saved transcript")
        with open(transcript_path, 'r', encoding='utf-8') as f:
            transcript_data = json.load(f)
        
        notes = transcript_data.get("notes", [])
        # Handle both snake_case and camelCase for backward compatibility
        transcript_text = transcript_data.get("transcriptText", transcript_data.get("transcript_text", ""))
        report_progress(
            "transcribe",
            "completed",
            percent=1.0,
            message=f"Loaded {len(notes)} notes from saved transcript",
        )
    else:
        # Step 1: Transcribe audio
        video_path = Path(args.input)
        if not video_path.exists():
            raise FileNotFoundError(f"Video not found at {video_path}")

        report_progress("transcribe", "running", message="Starting Whisper")
        transcript = load_transcript(video_path, args.model_size, args.language)
        segments = transcript.get("segments") or []
        notes = build_notes(segments)
        report_progress(
            "transcribe",
            "completed",
            percent=1.0,
            message=f"{len(notes)} timestamped notes ready",
        )

        # Build transcript text
        transcript_text = " ".join(segment.get("text", "").strip() for segment in segments).strip()
        
        # Debug: Output transcript to stderr
        sys.stderr.write("[TRANSCRIPT_START]\n")
        sys.stderr.write(transcript_text + "\n")
        sys.stderr.write("[TRANSCRIPT_END]\n")
        sys.stderr.flush()
        
        # Save transcript if transcript-only mode
        if args.transcript_only:
            transcript_data = {
                "notes": notes,
                "transcriptText": transcript_text,
            }
            output_path = video_path.with_suffix('.transcript.json')
            with open(output_path, 'w', encoding='utf-8') as f:
                json.dump(transcript_data, f, ensure_ascii=False, indent=2)
            
            print(json.dumps(transcript_data, ensure_ascii=False), flush=True)
            return

    # Step 2: Summarize transcript (skip if transcript-only mode)
    report_progress("summarize", "running", message="Summarizing decisions")
    sys.stdout.flush()
    sys.stderr.flush()
    
    try:
        summary = summarize(
            transcript_text,
            args.summary_model,
            args.max_summary_tokens,
            args.max_new_tokens,
            args.system_prompt,
            args.user_prompt_template,
            args.critical_instruction,
        )
    except Exception as e:
        report_progress("summarize", "failed", message=f"Summarization error: {str(e)}")
        raise
    
    report_progress("summarize", "completed", message="Summary ready")

    # Step 3: Output final result as JSON
    payload = {
        "notes": notes,
        "businessSummary": summary,
    }

    print(json.dumps(payload, ensure_ascii=False), flush=True)
    sys.stdout.flush()


if __name__ == "__main__":
    try:
        main()
    except Exception as exc:  # pragma: no cover
        report("pipeline", "failed", str(exc))
        raise

