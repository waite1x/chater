using Avalonia.Threading;
using SharpHook;
using SharpHook.Data;
using SharpHook.Providers;

namespace Chater.Services;

/// <summary>
/// Owns the process-wide keyboard hook and marshals matching shortcuts back to the UI thread.
/// </summary>
public sealed class GlobalHotKeyService(IWindowNavigationService navigation) : IGlobalHotKeyService
{
    private EventLoopGlobalHook? _hook;
    // These values are updated without recreating the hook so changing settings takes effect immediately.
    private string _chatShortcut = string.Empty;
    private string _newChatWindowShortcut = string.Empty;
    private bool _reportedMissingMacAccessibility;
    // Native hooks can report the same key press more than once; this timestamp debounces it.
    private long _lastTriggeredAt;

    /// <summary>Gets the most recent hook-start failure that can be shown to the user.</summary>
    public string? LastError { get; private set; }

    /// <summary>Starts listening for global shortcuts, if the operating system permits it.</summary>
    public bool Start(string chatShortcut, string newChatWindowShortcut)
    {
        _chatShortcut = chatShortcut;
        _newChatWindowShortcut = newChatWindowShortcut;
        if (_hook is not null)
        {
            return true;
        }

        if (OperatingSystem.IsMacOS() && !MacAccessibility.IsTrusted())
        {
            LastError = "macOS 未授予辅助功能权限，无法启用全局快捷键。已打开系统设置，请在 Chater 下开启权限后重启应用。";
            if (!_reportedMissingMacAccessibility)
            {
                _reportedMissingMacAccessibility = true;
                MacAccessibility.OpenSettings();
            }
            return false;
        }

        try
        {
            UioHookProvider.Instance.KeyTypedEnabled = false;
            _hook = new EventLoopGlobalHook(GlobalHookType.Keyboard);
            _hook.KeyPressed += OnKeyPressed;
            _ = RunAsync();
            LastError = null;
            return true;
        }
        catch (Exception exception)
        {
            LastError = $"全局快捷键启动失败：{exception.Message}";
            _hook?.Dispose();
            _hook = null;
            return false;
        }
    }

    public void UpdateShortcuts(string chatShortcut, string newChatWindowShortcut)
    {
        _chatShortcut = chatShortcut;
        _newChatWindowShortcut = newChatWindowShortcut;
    }

    private void OnKeyPressed(object? sender, KeyboardHookEventArgs e)
    {
        var current = Format(e.Data.KeyCode, e.RawEvent.Mask);
        if (current is null)
        {
            return;
        }

        var now = Environment.TickCount64;
        if (now - Interlocked.Read(ref _lastTriggeredAt) < 300)
        {
            return;
        }

        // The explicit "new window" action wins if both settings resolve to the same shortcut.
        if (ShortcutFormatter.Matches(_newChatWindowShortcut, current))
        {
            Interlocked.Exchange(ref _lastTriggeredAt, now);
            Dispatcher.UIThread.Post(navigation.ShowNewChat);
            return;
        }

        if (ShortcutFormatter.Matches(_chatShortcut, current))
        {
            Interlocked.Exchange(ref _lastTriggeredAt, now);
            Dispatcher.UIThread.Post(navigation.ShowChat);
        }
    }

    public void Dispose()
    {
        if (_hook is null)
        {
            return;
        }

        _hook.KeyPressed -= OnKeyPressed;
        _hook.Dispose();
        _hook = null;
    }

    private async Task RunAsync()
    {
        try
        {
            if (_hook is not null)
            {
                await _hook.RunAsync().ConfigureAwait(false);
            }
        }
        catch (Exception)
        {
            // Global hooks require OS permissions and, on Linux, an X11 session.
            // The tray and regular in-app shortcuts remain available if the hook cannot start.
        }
    }

    private static string? Format(KeyCode key, EventMask mask)
    {
        var keyName = key.ToString();
        if (!keyName.StartsWith("Vc", StringComparison.Ordinal))
        {
            return null;
        }

        keyName = keyName[2..];
        var parts = new List<string>(5);
        if ((mask & (EventMask.LeftCtrl | EventMask.RightCtrl)) != 0) parts.Add("Ctrl");
        if ((mask & (EventMask.LeftAlt | EventMask.RightAlt)) != 0) parts.Add("Alt");
        if ((mask & (EventMask.LeftShift | EventMask.RightShift)) != 0) parts.Add("Shift");
        if ((mask & (EventMask.LeftMeta | EventMask.RightMeta)) != 0) parts.Add("Meta");
        parts.Add(keyName);
        return string.Join('+', parts);
    }
}
