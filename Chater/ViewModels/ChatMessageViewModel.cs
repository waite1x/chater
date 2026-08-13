using Chater.AI.Conversations;
using System.Collections.ObjectModel;
using Avalonia.Threading;

namespace Chater.ViewModels;

public sealed partial class ChatMessageViewModel(MessageRole role, string content, IReadOnlyList<MessageAttachment>? attachments = null) : ViewModelBase
{
    private const string ResponseProgressNoticeId = "__response_progress";
    public MessageRole Role { get; } = role;
    public bool IsUser => Role == MessageRole.User;
    public bool IsAssistant => Role == MessageRole.Assistant;
    public IReadOnlyList<MessageAttachment> Attachments { get; } = attachments ?? [];
    public bool HasAttachments => Attachments.Count > 0;
    public ObservableCollection<ToolNoticeViewModel> ToolNotices { get; } = [];
    public bool HasToolNotices => ToolNotices.Count > 0;

    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty]
    private string _content = content;

    public void AddToolNotice(string callId, string text)
    {
        if (ToolNotices.Any(item => item.CallId == callId))
        {
            return;
        }

        ToolNotices.Add(new ToolNoticeViewModel(callId, text));
        OnPropertyChanged(nameof(HasToolNotices));
    }

    /// <summary>Updates the one transient notice that describes the model response lifecycle.</summary>
    public void SetResponseProgress(string text)
    {
        var notice = ToolNotices.FirstOrDefault(item => item.CallId == ResponseProgressNoticeId);
        if (notice is null)
        {
            AddToolNotice(ResponseProgressNoticeId, text);
            return;
        }

        notice.Text = text;
    }

    public void RemoveToolNotice(string callId)
    {
        var notice = ToolNotices.FirstOrDefault(item => item.CallId == callId);
        if (notice is not null)
        {
            ToolNotices.Remove(notice);
            OnPropertyChanged(nameof(HasToolNotices));
        }
    }

    public void CompleteToolNotice(string callId, string? completionText)
    {
        var notice = ToolNotices.FirstOrDefault(item => item.CallId == callId);
        if (notice is null)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(completionText))
        {
            notice.Text = completionText;
        }

        ScheduleToolNoticeRemoval(callId);
    }

    public void DismissToolNoticesAfterDelay()
    {
        foreach (var callId in ToolNotices.Select(static item => item.CallId).ToArray())
        {
            ScheduleToolNoticeRemoval(callId);
        }
    }

    private void ScheduleToolNoticeRemoval(string callId)
    {
        _ = RemoveToolNoticeAfterDelayAsync(callId);
    }

    private async Task RemoveToolNoticeAfterDelayAsync(string callId)
    {
        await Task.Delay(TimeSpan.FromMilliseconds(1400)).ConfigureAwait(false);
        Dispatcher.UIThread.Post(() => RemoveToolNotice(callId));
    }

    public void ClearToolNotices()
    {
        if (ToolNotices.Count == 0)
        {
            return;
        }

        ToolNotices.Clear();
        OnPropertyChanged(nameof(HasToolNotices));
    }
}
