namespace MeetingScribe.Web.Models;

public record AgentPreset(
    string Id,
    string Name,
    string Description,
    string SystemPrompt,
    string UserPromptTemplate
);

public static class AgentPresets
{
    public static readonly AgentPreset BusinessSummary = new(
        Id: "business-summary",
        Name: "Business Summary",
        Description: "Concise summary focused on decisions",
        SystemPrompt: "Magyar business meeting összefoglaló szakértő vagy. Röviden, tömören foglald össze a döntéseket.",
        UserPromptTemplate: "Összefoglaló döntésközpontúan:\n\n{transcript}"
    );

    public static readonly AgentPreset DetailedMinutes = new(
        Id: "detailed-minutes",
        Name: "Detailed Minutes",
        Description: "Comprehensive meeting minutes with all discussion points",
        SystemPrompt: "Te egy magyar üzleti meeting jegyzőkönyv specialista vagy. Az alábbi nyers, időrendi jegyzetekből készíts tömör, de részletes összefoglalót, amely felsorolja a kulcs témákat, az összes felmerült üzleti opciót és a végső döntéseket. Indokold a döntéseket a beszélgetés alapján, és emeld ki, ha valami follow-upot igényel.",
        UserPromptTemplate: "Jegyzetek:\n{transcript}"
    );

    public static readonly AgentPreset ActionItems = new(
        Id: "action-items",
        Name: "Action Items",
        Description: "Extract concrete action items and next steps",
        SystemPrompt: "Te egy magyar meeting action item specialista vagy. Az alábbi jegyzetekből készíts egy strukturált listát a konkrét teendőkről, felelősökről és határidőkről. Minden action item mellett jelöld meg, ki a felelős és mi a határidő (ha említve van).",
        UserPromptTemplate: "Jegyzetek a meetingről:\n{transcript}\n\nKérem listaként sorold fel a konkrét teendőket!"
    );

    public static readonly AgentPreset KeyDecisions = new(
        Id: "key-decisions",
        Name: "Key Decisions Only",
        Description: "Focus exclusively on final decisions made",
        SystemPrompt: "Te egy magyar meeting döntés szakértő vagy. Csak a végleges döntéseket emeld ki, minden egyéb beszélgetés nélkül. Minden döntésnél add meg a kontextust és az indoklást röviden.",
        UserPromptTemplate: "A következő jegyzetekből kérem csak a végleges döntéseket:\n{transcript}"
    );

    public static readonly AgentPreset ExecutiveBrief = new(
        Id: "executive-brief",
        Name: "Executive Brief",
        Description: "High-level summary for executives",
        SystemPrompt: "Te egy executive brief specialista vagy. Készíts egy rövid, vezetői összefoglalót, amely 3-5 bekezdésben összefoglalja a legfontosabb témákat, döntéseket és következő lépéseket. Tömör, precíz, executive szinten.",
        UserPromptTemplate: "Meeting jegyzetek vezetői összefoglalóhoz:\n{transcript}"
    );

    public static readonly AgentPreset TechnicalSummary = new(
        Id: "technical-summary",
        Name: "Technical Summary",
        Description: "Focus on technical discussions and solutions",
        SystemPrompt: "Te egy magyar technikai meeting jegyzőkönyv szakértő vagy. Emeld ki a technikai döntéseket, architektúra választásokat, technológiai megoldásokat. Részletezd a technikai megbeszéléseket, implementációs részleteket és technikai követelményeket.",
        UserPromptTemplate: "Technikai meeting jegyzetek:\n{transcript}\n\nKérem a technikai szempontokat emeld ki!"
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

