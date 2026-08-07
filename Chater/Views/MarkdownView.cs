using System.Globalization;
using System.Diagnostics;
using System.Net;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Chater.Logging;
using LiveMarkdown.Avalonia;
using Markdig;
using Markdig.Extensions.AutoLinks;
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

    static MarkdownView()
    {
        MarkdownRenderer.ConfigurePipeline += builder => builder.UseAutoLinks(new AutoLinkOptions
        {
            UseHttpsForWWWLinks = true
        });
    }

    public MarkdownView()
    {
        _renderer = new MarkdownRenderer
        {
            MarkdownBuilder = _builder,
            CodeBlockColorTheme = ThemeName.LightPlus
        };
        _renderer.LinkClick += OnLinkClick;
        Content = _renderer;
        ActualThemeVariantChanged += OnActualThemeVariantChanged;
        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel, true);
    }

    public string? Markdown
    {
        get => GetValue(MarkdownProperty);
        set => SetValue(MarkdownProperty, value);
    }

    public async Task CopySelectionWithFormattingAsync()
    {
        var selectedBlocks = _renderer.GetVisualDescendants()
            .OfType<MarkdownTextBlock>()
            .Where(block => !string.IsNullOrEmpty(block.SelectedText))
            .ToList();
        if (selectedBlocks.Count == 0 || TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard)
        {
            return;
        }

        var plainText = string.Join(Environment.NewLine, selectedBlocks.Select(block => block.SelectedText));
        var html = string.Concat(selectedBlocks.Select(ToHtml));
        var htmlFormat = OperatingSystem.IsWindows()
            ? DataFormat.CreateStringPlatformFormat("HTML Format")
            : DataFormat.CreateStringPlatformFormat(OperatingSystem.IsMacOS() ? "public.html" : "text/html");

        var item = DataTransferItem.CreateText(plainText);
        item.Set(htmlFormat, OperatingSystem.IsWindows() ? ToWindowsClipboardHtml(html) : html);
        var data = new DataTransfer();
        data.Add(item);
        await clipboard.SetDataAsync(data);
    }

    private async void OnKeyDown(object? sender, KeyEventArgs e)
    {
        var commandModifier = OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control;
        if (e.Key != Key.C || !e.KeyModifiers.HasFlag(commandModifier) || !_renderer.CanCopy)
        {
            return;
        }

        e.Handled = true;
        await CopySelectionWithFormattingAsync();
    }

    private static string ToHtml(MarkdownTextBlock block)
    {
        var text = WebUtility.HtmlEncode(block.SelectedText).Replace("\r\n", "<br>").Replace("\n", "<br>");
        var size = block.FontSize.ToString("0.##", CultureInfo.InvariantCulture);
        var weight = ToCssFontWeight(block.FontWeight);
        var style = block.FontStyle == Avalonia.Media.FontStyle.Italic ? "italic" : "normal";
        return $"<div style=\"font-family:{WebUtility.HtmlEncode(block.FontFamily.Name)};font-size:{size}px;font-weight:{weight};font-style:{style};white-space:pre-wrap\">{text}</div>";
    }

    private static int ToCssFontWeight(FontWeight weight) => weight switch
    {
        _ when weight == FontWeight.Thin => 100,
        _ when weight == FontWeight.ExtraLight => 200,
        _ when weight == FontWeight.Light => 300,
        _ when weight == FontWeight.Medium => 500,
        _ when weight == FontWeight.SemiBold => 600,
        _ when weight == FontWeight.Bold => 700,
        _ when weight == FontWeight.ExtraBold => 800,
        _ when weight == FontWeight.Black => 900,
        _ => 400
    };

    private static string ToWindowsClipboardHtml(string fragment)
    {
        const string headerTemplate = "Version:0.9\r\nStartHTML:{0:D10}\r\nEndHTML:{1:D10}\r\nStartFragment:{2:D10}\r\nEndFragment:{3:D10}\r\n";
        const string start = "<html><body><!--StartFragment-->";
        const string end = "<!--EndFragment--></body></html>";
        var emptyHeader = string.Format(CultureInfo.InvariantCulture, headerTemplate, 0, 0, 0, 0);
        var startHtml = Encoding.UTF8.GetByteCount(emptyHeader);
        var startFragment = startHtml + Encoding.UTF8.GetByteCount(start);
        var endFragment = startFragment + Encoding.UTF8.GetByteCount(fragment);
        var endHtml = endFragment + Encoding.UTF8.GetByteCount(end);
        return string.Format(CultureInfo.InvariantCulture, headerTemplate, startHtml, endHtml, startFragment, endFragment) + start + fragment + end;
    }

    private void OnActualThemeVariantChanged(object? sender, EventArgs e)
    {
        _renderer.CodeBlockColorTheme = ActualThemeVariant == ThemeVariant.Dark
            ? ThemeName.DarkPlus
            : ThemeName.LightPlus;
    }

    private static void OnLinkClick(object? sender, LinkClickedEventArgs e)
    {
        if (e.HRef is not { Scheme: "http" or "https" } uri)
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
            e.Handled = true;
        }
        catch (Exception exception)
        {
            ExceptionLogger.Log(exception, nameof(MarkdownView), "Failed to open a markdown link", Microsoft.Extensions.Logging.LogLevel.Warning);
            // Opening a link is best-effort; an unavailable system browser must not crash the chat window.
        }
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
