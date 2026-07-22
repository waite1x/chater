using Avalonia;
using Avalonia.Controls;
using LiveMarkdown.Avalonia;

namespace Chater.Views;

/// <summary>
/// Binding-friendly adapter for LiveMarkdown's streaming renderer.
/// </summary>
public sealed class MarkdownView : UserControl
{
    public static readonly StyledProperty<string?> MarkdownProperty =
        AvaloniaProperty.Register<MarkdownView, string?>(nameof(Markdown));

    private readonly ObservableStringBuilder _builder = new();
    private string _renderedMarkdown = string.Empty;

    public MarkdownView()
    {
        var renderer = new MarkdownRenderer
        {
            MarkdownBuilder = _builder
        };
        Content = renderer;
    }

    public string? Markdown
    {
        get => GetValue(MarkdownProperty);
        set => SetValue(MarkdownProperty, value);
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
