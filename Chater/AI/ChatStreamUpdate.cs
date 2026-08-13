namespace Chater.AI;

public enum ChatStreamUpdateKind
{
    Text,
    Progress,
    ToolStarted,
    ToolCompleted
}

/// <summary>A streamed UI update. Only <see cref="ChatStreamUpdateKind.Text"/> is persisted.</summary>
public sealed record ChatStreamUpdate(ChatStreamUpdateKind Kind, string? Content = null, string? ToolCallId = null)
{
    public static ChatStreamUpdate Text(string content) => new(ChatStreamUpdateKind.Text, content);
    /// <summary>Creates a transient progress update whose content is a localization resource key.</summary>
    public static ChatStreamUpdate Progress(string resourceKey) => new(ChatStreamUpdateKind.Progress, resourceKey);
    public static ChatStreamUpdate ToolStarted(string callId, string notice) =>
        new(ChatStreamUpdateKind.ToolStarted, notice, callId);
    public static ChatStreamUpdate ToolCompleted(string callId, string? notice = null) =>
        new(ChatStreamUpdateKind.ToolCompleted, notice, callId);
}
