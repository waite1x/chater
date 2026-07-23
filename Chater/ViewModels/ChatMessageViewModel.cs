using Chater.Models.Enums;

namespace Chater.ViewModels;

public sealed partial class ChatMessageViewModel(MessageRole role, string content) : ViewModelBase
{
    public MessageRole Role { get; } = role;
    public string Sender => Role == MessageRole.User ? "你" : "AI";

    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty]
    private string _content = content;
}
