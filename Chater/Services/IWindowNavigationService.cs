namespace Chater.Services;

public interface IWindowNavigationService
{
    void ShowSettings();
    void ShowSkillSettings();
    void ShowChat();
    void ShowNewChat() => ShowChat();
    void ShowChat(string? conversationId) => ShowChat();
}
