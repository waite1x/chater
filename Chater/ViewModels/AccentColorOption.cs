using Avalonia.Media;

namespace Chater.ViewModels;

public sealed record AccentColorOption(string Key, string DisplayName, string Hex)
{
    public Color Color => Color.Parse(Hex);
    public IBrush Brush => new SolidColorBrush(Color);
}
