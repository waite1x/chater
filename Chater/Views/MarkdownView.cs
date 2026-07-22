using Avalonia;
using Avalonia.Controls;
using Avalonia.Styling;
using LiveMarkdown.Avalonia;
using TextMateSharp.Grammars;

namespace Chater.Views;

/// <summary>
/// Binding-friendly adapter for LiveMarkdown's streaming renderer.
/// </summary>
public sealed class MarkdownView : UserControl
{
    public static readonly StyledProperty<string?> MarkdownProperty =
        AvaloniaProperty.Register<MarkdownView, string?>(nameof(Markdown));

    private readonly ObservableStringBuilder _builder = new();
    private readonly MarkdownRenderer _renderer;
    private string _renderedMarkdown = string.Empty;

    public MarkdownView()
    {
        _renderer = new MarkdownRenderer
        {
            MarkdownBuilder = _builder,
            CodeBlockColorTheme = ThemeName.LightPlus
        };
        Content = _renderer;
        ActualThemeVariantChanged += OnActualThemeVariantChanged;
    }

    public string? Markdown
    {
        get => GetValue(MarkdownProperty);
        set => SetValue(MarkdownProperty, value);
    }

    private void OnActualThemeVariantChanged(object? sender, EventArgs e)
    {
        _renderer.CodeBlockColorTheme = ActualThemeVariant == ThemeVariant.Dark
            ? ThemeName.DarkPlus
            : ThemeName.LightPlus;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property != MarkdownProperty)
        {
            return;
        }

        var markdown = change.NewValue is string value ? value : string.Empty;
        if (markdown.StartsWith(_renderedMarkdown, StringComparison.Ordinal))
        {
            _builder.Append(markdown[_renderedMarkdown.Length..]);
        }
        else
        {
            _builder.Clear();
            _builder.Append(markdown);
        }

        _renderedMarkdown = markdown;
    }
}
