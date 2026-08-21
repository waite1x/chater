using Avalonia.Controls;
using Avalonia.Media;
using LiveMarkdown.Avalonia;
using Markdig.Syntax;

namespace Chater.Views;

/// <summary>Renders each ordered <c>thinking</c> fence as an independently collapsible block.</summary>
public sealed class ThinkingBlockNode : BlockNode<FencedCodeBlock>
{
    private readonly ObservableStringBuilder _builder = new();
    private readonly MarkdownRenderer _renderer;
    private readonly ThinkingBlockControl _control;

    public override Control Control => _control;

    public ThinkingBlockNode()
    {
        _renderer = new MarkdownRenderer
        {
            MarkdownBuilder = _builder
        };

        _control = new ThinkingBlockControl(_renderer)
        {
            IsExpanded = false,
            Padding = new Avalonia.Thickness(0),
            Margin = new Avalonia.Thickness(0, 0, 0, 2),
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch
        };
    }

    protected override bool MatchesBlock(FencedCodeBlock block) =>
        string.Equals(block.Info?.Trim(), "thinking", StringComparison.OrdinalIgnoreCase);

    protected override bool UpdateCore(
        DocumentNode documentNode,
        FencedCodeBlock block,
        in ObservableStringBuilderChangedEventArgs change,
        CancellationToken cancellationToken)
    {
        if (block.Lines.Lines is null)
        {
            return false;
        }

        var text = string.Join(
            Environment.NewLine,
            block.Lines.Lines.Take(block.Lines.Count).Select(line => line.Slice.ToString()));

        _builder.Clear();
        _builder.Append(text);
        return true;
    }
}

/// <summary>
/// Hosts a concrete <see cref="Expander"/> so Avalonia's theme applies its control template.
/// Theme type selectors do not automatically apply an Expander template to subclasses.
/// </summary>
public sealed class ThinkingBlockControl : Border
{
    private readonly Expander _expander;
    private readonly TextBlock _indicator = new()
    {
        Text = "▸",
        FontSize = 11,
        Width = 14,
        TextAlignment = Avalonia.Media.TextAlignment.Center,
        Opacity = 0.68
    };
    private readonly TextBlock _header = new()
    {
        FontSize = 11,
        FontWeight = FontWeight.Medium,
        Opacity = 0.68,
        Margin = new Avalonia.Thickness(0)
    };
    private string _defaultHeader = "Thinking";

    public ThinkingBlockControl(MarkdownRenderer renderer)
    {
        _header.Text = _defaultHeader;
        _expander = new Expander
        {
            Header = new StackPanel
            {
                Orientation = Avalonia.Layout.Orientation.Horizontal,
                Spacing = 3,
                Margin = new Avalonia.Thickness(0),
                Children = { _indicator, _header }
            },
            IsExpanded = false,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
            HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
            VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Top,
            Padding = new Avalonia.Thickness(0),
            Content = new Border
            {
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
                Child = renderer
            }
        };
        _expander.Classes.Add("thinking-block");

        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch;
        renderer.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch;
        Child = _expander;
        _expander.Expanded += OnExpanded;
        _expander.Collapsed += OnCollapsed;
    }

    public bool IsExpanded
    {
        get => _expander.IsExpanded;
        set => _expander.IsExpanded = value;
    }

    private void OnExpanded(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => _indicator.Text = "▾";

    private void OnCollapsed(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => _indicator.Text = "▸";

    public void SetDefaultHeader(string? header)
    {
        if (!string.IsNullOrWhiteSpace(header))
        {
            _defaultHeader = header;
        }

        if (!_expander.Classes.Contains("thinking-active"))
        {
            _header.Text = _defaultHeader;
        }
    }

    public void SetActive(bool active, string? status)
    {
        if (active && !string.IsNullOrWhiteSpace(status))
        {
            _expander.Classes.Add("thinking-active");
            _header.Text = status.EndsWith('…') ? status : status + "…";
            return;
        }

        _expander.Classes.Remove("thinking-active");
        _header.Text = _defaultHeader;
    }
}
