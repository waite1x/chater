using Avalonia;
using Avalonia.Styling;
using Chater.Data;

namespace Chater.Services;

public sealed class AppSettingsService(AppSettingRepository repository)
{
    public const string ThemeKey = "theme";
    public const string ChatShortcutKey = "chat.shortcut";
    public const string DefaultTheme = "system";
    public const string DefaultChatShortcut = "Ctrl+Shift+Space";

    public Task<string?> GetAsync(string key, CancellationToken cancellationToken = default) => repository.GetAsync(key, cancellationToken);
    public Task SaveAsync(string key, string value, CancellationToken cancellationToken = default) => repository.SaveAsync(key, value, cancellationToken);

    public static void ApplyTheme(string theme)
    {
        if (Application.Current is null)
        {
            return;
        }

        Application.Current.RequestedThemeVariant = theme switch
        {
            "light" => ThemeVariant.Light,
            "dark" => ThemeVariant.Dark,
            _ => ThemeVariant.Default
        };
    }
}
