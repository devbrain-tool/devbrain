namespace DevBrain.Core.Enums;

public enum EventType
{
    // Existing
    ToolCall,
    FileChange,
    Decision,
    Error,
    Conversation,

    // New — rich capture
    ToolFailure,
    UserPrompt,
    SessionStart,
    SessionEnd,
    TurnComplete,
    TurnError,
    SubagentStart,
    SubagentStop,
    CwdChange,
    ContextCompact,
}
