using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using LiveMarkdown.Avalonia;
using Markdig.Syntax;
using Material.Icons;
using Material.Icons.Avalonia;

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
/// A compact, header-clickable container for model reasoning and tool notices.
/// </summary>
public sealed class ThinkingBlockControl : Border
{
    private readonly Border _header;
    private readonly Border _content;
    private readonly MaterialIcon _indicator = new()
    {
        Kind = MaterialIconKind.ChevronRight,
        Width = 12,
        Height = 12,
        Opacity = 0.64,
        VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
    };
    private readonly TextBlock _headerText = new()
    {
        FontSize = 14,
        FontWeight = FontWeight.Medium,
        Opacity = 0.68,
        Margin = new Avalonia.Thickness(0)
    };
    private string _defaultHeader = "Thinking";
    private bool _isExpanded;

    public ThinkingBlockControl(MarkdownRenderer renderer)
    {
        _headerText.Text = _defaultHeader;

        _header = new Border
        {
            Padding = new Avalonia.Thickness(2, 1),
            Background = Brushes.Transparent,
            Cursor = new Cursor(StandardCursorType.Hand),
            Child = new StackPanel
            {
                Orientation = Avalonia.Layout.Orientation.Horizontal,
                Spacing = 4,
                Margin = new Avalonia.Thickness(0),
                Children = { _indicator, _headerText }
            }
        };

        _content = new Border
        {
            IsVisible = false,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
            Child = renderer
        };

        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch;
        renderer.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch;
        Child = new StackPanel
        {
            Spacing = 0,
            Children = { _header, _content }
        };
        _header.PointerPressed += OnHeaderPointerPressed;
    }

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (_isExpanded == value)
            {
                return;
            }

            _isExpanded = value;
            _content.IsVisible = value;
            _indicator.Kind = value ? MaterialIconKind.ChevronDown : MaterialIconKind.ChevronRight;
        }
    }

    private void OnHeaderPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(_header).Properties.IsLeftButtonPressed)
        {
            return;
        }

        IsExpanded = !IsExpanded;
        e.Handled = true;
    }

    public void SetDefaultHeader(string? header)
    {
        if (!string.IsNullOrWhiteSpace(header))
        {
            _defaultHeader = header;
        }

        if (!Classes.Contains("thinking-active"))
        {
            _headerText.Text = _defaultHeader;
        }
    }

    public void SetActive(bool active, string? status)
    {
        if (active && !string.IsNullOrWhiteSpace(status))
        {
            Classes.Add("thinking-active");
            _headerText.Text = status.EndsWith('…') ? status : status + "…";
            return;
        }

        Classes.Remove("thinking-active");
        _headerText.Text = _defaultHeader;
    }
}
