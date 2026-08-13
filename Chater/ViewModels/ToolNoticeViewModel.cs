namespace Chater.ViewModels;

public sealed partial class ToolNoticeViewModel(string callId, string text) : ViewModelBase
{
    public string CallId { get; } = callId;

    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty]
    private string _text = text;
}
