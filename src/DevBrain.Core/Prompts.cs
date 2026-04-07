namespace DevBrain.Core;

public static class Prompts
{
    /// <summary>
    /// Safely fills named placeholders in a prompt template.
    /// Uses {NAME} syntax instead of {0} to avoid FormatException
    /// when user content contains braces (common in C# code).
    /// </summary>
    public static string Fill(string template, params (string key, string value)[] replacements)
    {
        var result = template;
        foreach (var (key, value) in replacements)
            result = result.Replace($"{{{key}}}", value);
        return result;
    }

    // -- Feature 2: Session Storytelling --

    public const string StorytellerNarrative = """
        Write a developer session narrative from these events.

        Session duration: {DURATION}
        Phases: {PHASES}
        Turning points: {TURNING_POINTS}

        Events:
        {EVENTS}

        Rules:
        - Past tense, third person ("The developer...")
        - Structure: Goal -> Approach -> Obstacles -> Resolution
        - Mention specific files and decisions by name
        - Note dead ends and what they taught
        - End with one-line "session outcome"
        - Under 300 words
        """;

    // -- Feature 3: Decision Replay --

    public const string DecisionClassification = """
        Given two developer decisions about the same codebase, classify their relationship.

        Decision A (earlier): {DECISION_A}
        Decision B (later): {DECISION_B}
        Shared files: {SHARED_FILES}

        Classify as ONE of:
        - caused_by: B was motivated by A
        - supersedes: B replaces A
        - resolved_by: B resolves a problem A introduced
        - unrelated: no causal connection

        Respond with ONLY the classification label.
        """;

    public const string DecisionChainNarrative = """
        Explain why this code exists by narrating the chain of decisions that led to it.

        File: {FILE}
        Decision chain (chronological):
        {CHAIN}

        Rules:
        - Explain the "why" behind each decision
        - Note alternatives that were rejected and why
        - Highlight dead ends that were hit along the way
        - Keep under 200 words
        """;

    // -- Feature 4: Blast Radius Prediction --

    public const string BlastRadiusSummary = """
        Summarize the potential impact of changing this file.

        File being changed: {FILE}
        Affected files and their connection:
        {AFFECTED}
        Dead ends at risk of re-triggering:
        {DEAD_ENDS}

        Rules:
        - Focus on the highest-risk impacts
        - Explain WHY each file is affected (the decision that connects them)
        - Warn about dead ends that could resurface
        - Keep under 150 words
        """;

    // -- Feature 5: Growth Tracker - Complexity --

    public const string ComplexityClassification = """
        Rate the complexity of this development task from 1-5:
        1 = Routine (config changes, simple CRUD, renames)
        2 = Moderate (new feature with clear requirements)
        3 = Significant (cross-cutting changes, new abstractions)
        4 = Complex (architectural decisions, novel algorithms)
        5 = Expert (system design, performance-critical, multi-system integration)

        Thread summary: {SUMMARY}
        Files changed: {FILES}
        Decisions made: {DECISIONS}
        Errors encountered: {ERRORS}

        Respond with ONLY the number.
        """;

    // -- Feature 5: Growth Tracker - Quality --

    public const string ErrorClassification = """
        Classify this development error into ONE category:
        - logic_bug: incorrect logic, wrong algorithm, bad assumption
        - typo: syntax error, misspelling, wrong variable name
        - environment: config issue, missing dependency, wrong version
        - external: third-party API failure, network issue, dependency bug

        Error: {ERROR}
        Context: {CONTEXT}

        Respond with ONLY the category.
        """;

    // -- Feature 5: Growth Tracker - Weekly Narrative --

    public const string GrowthNarrative = """
        Given these developer metrics for the past week, write 2-3 sentences
        highlighting the most interesting trend or achievement.

        Metrics: {METRICS}
        Milestones: {MILESTONES}
        4-week trend: {TREND}
        Complexity trend: {COMPLEXITY}
        Quality trend: {QUALITY}
        Error breakdown: {ERROR_BREAKDOWN}

        Rules:
        - Be encouraging but honest
        - Focus on growth, not absolute numbers
        - Never compare to other developers
        - If complexity is rising while quality holds, highlight this prominently
        - Keep under 100 words
        """;

    // -- Existing: Briefing Agent (migrated from BriefingAgent.cs) --

    public const string BriefingGeneration = """
        Generate a daily development briefing based on the following observations from the last 24 hours.
        Summarize key decisions, errors encountered, files changed, and overall progress.
        Format as markdown with sections.

        Observations:
        {OBSERVATIONS}
        """;

    // -- Existing: Compression Agent (migrated from CompressionAgent.cs) --

    public const string CompressionSummarization = """
        Summarize the following development observation concisely:

        {CONTENT}
        """;
}
