namespace Chater.AI.Conversations;

public sealed record Message(
    string Id,
    string ConversationId,
    long SequenceNo,
    MessageRole Role,
    string Content,
    MessageStatus Status,
    string? ErrorCode,
    string? ErrorMessage,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
