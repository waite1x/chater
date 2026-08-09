using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Chater.Logging;
using Chater.Services;
using Chater.ViewModels;
using Microsoft.Extensions.Logging;

namespace Chater.Views;

public partial class ChatWindow : Window
{
    private readonly ChatWindowViewModel _viewModel;
    private readonly IGlobalHotKeyService _globalHotKeys;
    private readonly ILogger<ChatWindow> _logger;
    private readonly TaskCompletionSource _initializedTcs = new();

    /// <summary>Completes when the ViewModel has finished its initial <see cref="ChatWindowViewModel.LoadAsync"/>.</summary>
    public Task Initialization => _initializedTcs.Task;

    /// <summary>If set before the window is shown, this conversation will be opened after loading.</summary>
    internal string? PendingConversationId { get; set; }

    protected override bool HideOnClose => false;

    public ChatWindow(ChatWindowViewModel viewModel, IGlobalHotKeyService globalHotKeys, ILogger<ChatWindow> logger)
    {
        _viewModel = viewModel;
        _globalHotKeys = globalHotKeys;
        _logger = logger;
        DataContext = viewModel;
        viewModel.AttachStorageProvider(StorageProvider);
        InitializeComponent();
        DraftTextBox.AddHandler(KeyDownEvent, OnDraftKeyDown, RoutingStrategies.Tunnel);
        ConfigurePlatformTitleBar();
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        _ = InitializeAsync();
    }

    private async Task InitializeAsync()
    {
        try
        {
            // The window is shown first so long-running I/O never delays native window creation.
            if (PendingConversationId is not null)
            {
                await _viewModel.OpenConversationAsync(PendingConversationId).ConfigureAwait(true);
                PendingConversationId = null;
            }

            // Register global hotkeys after the ViewModel has loaded its shortcut settings.
            if (!_globalHotKeys.Start(_viewModel.ChatShortcut, _viewModel.NewChatWindowShortcut) && _globalHotKeys.LastError is not null)
            {
                _viewModel.StatusMessage = _globalHotKeys.LastError;
                _logger.LogWarning("Global hotkey registration failed: {Error}", _globalHotKeys.LastError);
            }
        }
        catch (Exception exception)
        {
            ExceptionLogger.Log(exception, nameof(ChatWindow), "Failed to initialize a chat window");
            _viewModel.StatusMessage = exception.Message;
        }
        finally
        {
            _initializedTcs.TrySetResult();
        }
    }

    private void OnDraftTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (sender is TextBox textBox)
        {
            _viewModel.Draft = textBox.Text ?? string.Empty;
        }
    }

    private void OnDraftKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            return;
        }

        if (_viewModel.SendCommand.CanExecute(null))
        {
            _viewModel.SendCommand.Execute(null);
        }

        // Prevent TextBox from inserting a newline and prevent the window's
        // default button handling from processing the same key press.
        e.Handled = true;
    }

    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
            return;
        }

        if (_viewModel.IsChatShortcut(e.Key, e.KeyModifiers))
        {
            _viewModel.ShowChatCommand.Execute(null);
            e.Handled = true;
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        // The base window disposes the ViewModel (DataContext) and the scope,
        // which triggers full cleanup of all window-scoped resources.
        base.OnClosed(e);
    }

}
