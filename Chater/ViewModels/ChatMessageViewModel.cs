using Chater.AI.Conversations;

namespace Chater.ViewModels;

public sealed partial class ChatMessageViewModel(MessageRole role, string content) : ViewModelBase
{
    public MessageRole Role { get; } = role;
    public string Sender => Role == MessageRole.User ? "你" : "AI";
    public bool IsUser => Role == MessageRole.User;
    public bool IsAssistant => Role == MessageRole.Assistant;

    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty]
    private string _content = content;
}
