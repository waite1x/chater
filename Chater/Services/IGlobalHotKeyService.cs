namespace Chater.Services;

public interface IGlobalHotKeyService : IDisposable
{
    bool Start(string shortcut);
    void UpdateShortcut(string shortcut);
    string? LastError { get; }
}
