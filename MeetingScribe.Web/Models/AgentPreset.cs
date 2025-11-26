namespace MeetingScribe.Web.Models;

public record AgentPreset(
    string Id,
    string Name,
    string Description,
    string SystemPrompt,
    string UserPromptTemplate,
    string CriticalInstruction
);

public static class AgentPresets
{
    // Critical instruction - ALWAYS appended to system prompts
    public const string DefaultCriticalInstruction = @"═══════════════════════════════════════════════════════════════
                                                       ⚠️ CRITICAL INSTRUCTION - READ THIS CAREFULLY ⚠️
                                                       ═══════════════════════════════════════════════════════════════

                                                       OUTPUT LANGUAGE: You MUST write your ENTIRE response in ENGLISH.
                                                                       Do NOT write any Hungarian text in your output.
                                                                       ENGLISH ONLY - no exceptions.

                                                       INPUT LANGUAGE:  The transcript you receive is in Hungarian.
                                                                       Your job: READ Hungarian → WRITE English

                                                       PROCESS:
                                                       Step 1: Read and understand the Hungarian transcript
                                                       Step 2: Comprehend the meaning and context
                                                       Step 3: Generate your response in ENGLISH

                                                       UNKNOWN WORDS: If Hungarian words are unclear, use context clues 
                                                                   to infer meaning intelligently.

                                                       ═══════════════════════════════════════════════════════════════
                                                       REMEMBER: Input = Hungarian (read only) | Output = ENGLISH (write)
                                                       ═══════════════════════════════════════════════════════════════";

    public static readonly AgentPreset BusinessSummary = new(
        Id: "business-summary",
        Name: "Business Summary",
        Description: "Concise summary focused on decisions",
        SystemPrompt: "You are a business meeting summary expert. Create concise, decision-focused summaries.",
        UserPromptTemplate: "Create a concise business summary focused on decisions:\n\n{transcript}",
        CriticalInstruction: DefaultCriticalInstruction
    );

    public static readonly AgentPreset DetailedMinutes = new(
        Id: "detailed-minutes",
        Name: "Detailed Minutes",
        Description: "Comprehensive meeting minutes with all discussion points",
        SystemPrompt: "You are a business meeting minutes specialist. From the raw, chronological notes, create a concise but detailed summary that lists key topics, all business options discussed, and final decisions. Justify decisions based on the conversation and highlight anything requiring follow-up.",
        UserPromptTemplate: "Create detailed meeting minutes from these notes:\n\n{transcript}",
        CriticalInstruction: DefaultCriticalInstruction
    );

    public static readonly AgentPreset ActionItems = new(
        Id: "action-items",
        Name: "Action Items",
        Description: "Extract concrete action items and next steps",
        SystemPrompt: "You are a meeting action items specialist. From the notes, create a structured list of concrete tasks, responsibilities, and deadlines. For each action item, indicate who is responsible and the deadline (if mentioned).",
        UserPromptTemplate: "Extract all action items from this meeting:\n\n{transcript}\n\nList all concrete tasks with owners and deadlines.",
        CriticalInstruction: DefaultCriticalInstruction
    );

    public static readonly AgentPreset KeyDecisions = new(
        Id: "key-decisions",
        Name: "Key Decisions Only",
        Description: "Focus exclusively on final decisions made",
        SystemPrompt: "You are a meeting decisions specialist. Highlight only the final decisions made, without other discussion. For each decision, provide context and brief justification.",
        UserPromptTemplate: "Extract only the final decisions from these notes:\n\n{transcript}",
        CriticalInstruction: DefaultCriticalInstruction
    );

    public static readonly AgentPreset ExecutiveBrief = new(
        Id: "executive-brief",
        Name: "Executive Brief",
        Description: "High-level summary for executives",
        SystemPrompt: "You are an executive brief specialist. Create a short executive summary in 3-5 paragraphs covering the most important topics, decisions, and next steps. Be concise, precise, and executive-level.",
        UserPromptTemplate: "Create an executive brief from this meeting:\n\n{transcript}",
        CriticalInstruction: DefaultCriticalInstruction
    );

    public static readonly AgentPreset TechnicalSummary = new(
        Id: "technical-summary",
        Name: "Technical Summary",
        Description: "Focus on technical discussions and solutions",
        SystemPrompt: "You are a technical meeting notes specialist. Highlight technical decisions, architecture choices, and technology solutions. Detail technical discussions, implementation details, and technical requirements.",
        UserPromptTemplate: "Extract technical details from this meeting:\n\n{transcript}\n\nFocus on technical aspects and solutions discussed.",
        CriticalInstruction: DefaultCriticalInstruction
    );

    public static IReadOnlyList<AgentPreset> All { get; } = new[]
    {
        BusinessSummary,
        DetailedMinutes,
        ActionItems,
        KeyDecisions,
        ExecutiveBrief,
        TechnicalSummary
    };

    public static AgentPreset? GetById(string id) =>
        All.FirstOrDefault(p => p.Id == id);
}

