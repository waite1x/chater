using Chater.AI.Conversations;

namespace Chater.ViewModels;

public sealed partial class ChatMessageViewModel(MessageRole role, string content, IReadOnlyList<MessageAttachment>? attachments = null) : ViewModelBase
{
    public MessageRole Role { get; } = role;
    public bool IsUser => Role == MessageRole.User;
    public bool IsAssistant => Role == MessageRole.Assistant;
    public IReadOnlyList<MessageAttachment> Attachments { get; } = attachments ?? [];
    public bool HasAttachments => Attachments.Count > 0;

    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty]
    private string _content = content;
}
