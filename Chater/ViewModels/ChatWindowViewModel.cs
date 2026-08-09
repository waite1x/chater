using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Avalonia.Input;
using Avalonia.Threading;
using Chater.AI;
using Chater.AI.Conversations;
using Chater.AI.Providers;
using Chater.AI.Skills;
using Chater.Data;
using Chater.Localization;
using Chater.Logging;
using Chater.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Material.Icons;
using Microsoft.Extensions.Logging;

namespace Chater.ViewModels;

/// <summary>
/// Presentation model for the chat window. Each <see cref="Views.ChatWindow"/> receives its own
/// instance via scoped injection within a dedicated DI scope.
/// </summary>
/// <remarks>
/// <see cref="AppState"/> is the only shared state so preferences and update progress remain
/// consistent across windows without sharing transient conversation state.
/// </remarks>
public sealed partial class ChatWindowViewModel : ViewModelBase
{
    private readonly ConversationService _conversations;
    private readonly ChatService _chat;
    private readonly ConversationRepository _conversationRepository;
    private readonly MessageRepository _messageRepository;
    private readonly ChatWindowManager _chatWindowManager;
    private readonly IWindowNavigationService? _navigation;
    private readonly LocalizationService _localization;
    private readonly IGlobalHotKeyService? _globalHotKeys;
    private Conversation? _conversation;
    private CancellationTokenSource? _sendCancellation;
    private bool _openingConversation;
    private string? _lastSelectedSkillId;

    public ChatWindowViewModel(
        ConversationService conversations,
        ChatService chat,
        ConversationRepository conversationRepository,
        MessageRepository messageRepository,
        AppState appState,
        ChatWindowManager chatWindowManager,
        IWindowNavigationService? navigation = null,
        IGlobalHotKeyService? globalHotKeys = null,
        LocalizationService? localization = null
    )
    {
        AppState = appState;
        _conversations = conversations;
        _chat = chat;
        _conversationRepository = conversationRepository;
        _messageRepository = messageRepository;
        _chatWindowManager = chatWindowManager;
        _navigation = navigation;
        _localization = localization ?? new LocalizationService();
        _globalHotKeys = globalHotKeys;
        // Re-validate the dropdown selection whenever the shared skills list is
        // reloaded (e.g. after editing skills in the settings window).
        AppState.Skills.CollectionChanged += OnSkillsCollectionChanged;

        // Keep the window's shortcut bindings in sync with the shared app state so
        // the global hotkey hook always uses the user's configured shortcuts. This
        // also corrects the values once AppState finishes loading at startup.
        ChatShortcut = AppState.ChatShortcut;
        NewChatWindowShortcut = AppState.NewChatWindowShortcut;
        AppState.PropertyChanged += OnAppStatePropertyChanged;
    }

    public AppState AppState { get; }

    // ── Collections for chat UI ──────────────────────────────────────────

    public ObservableCollection<ChatMessageViewModel> Messages { get; } = [];

    // ── Chat-bound selections ────────────────────────────────────────────

    [ObservableProperty] private ApiProvider? _selectedProvider;

    [ObservableProperty] private string? _selectedModelId;

    [ObservableProperty] private Conversation? _selectedConversation;

    [ObservableProperty] private Skill? _selectedSkill;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    [NotifyCanExecuteChangedFor(nameof(SendOrStopCommand))]
    private string _draft = string.Empty;

    [ObservableProperty] private string _statusMessage = "Loading...";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    [NotifyCanExecuteChangedFor(nameof(SendOrStopCommand))]
    private bool _isSending;

    // ── Derived chat properties ──────────────────────────────────────────

    public IReadOnlyList<string> AvailableModels => SelectedProvider?.ModelIds ?? [];
    public LocalizationService Localization => _localization;
    public string SelectedProviderDisplayName => SelectedProvider?.Name ?? T("SelectApiKey");
    public string SelectedModelDisplayName => SelectedModelId ?? SelectedProvider?.ModelId ?? T("ModelPlaceholder");
    public string SendButtonText => IsSending ? T("Stop") : T("Send");
    public MaterialIconKind SendButtonIcon => IsSending ? MaterialIconKind.Stop : MaterialIconKind.Send;


    // ── Shortcuts (shared with global hotkey system) ─────────────────────

    [ObservableProperty] private string _chatShortcut = AppSettingsService.DefaultChatShortcut;

    [ObservableProperty] private string _newChatWindowShortcut = AppSettingsService.DefaultNewChatWindowShortcut;

    // ── Chat actions ─────────────────────────────────────────────────────

    [RelayCommand]
    private void NewConversation()
    {
        _conversation = null;
        SelectedConversation = null;
        Messages.Clear();
        SelectedSkill = AppState.Skills.FirstOrDefault();
        StatusMessage = T("NewConversationStatus");
    }

    /// <summary>
    /// Prepares a fresh, empty session for a newly opened chat window, defaulting the
    /// selected skill to the first available one. If the skills list is not loaded yet,
    /// the skills collection change handler completes the default selection once loaded.
    /// </summary>
    public void PrepareNewSession()
    {
        _conversation = null;
        SelectedConversation = null;
        Messages.Clear();
        if (AppState.Skills.Count > 0)
        {
            SelectedSkill = AppState.Skills[0];
        }

        if (AppState.Providers.Count > 0 && SelectedProvider == null)
        {
            SelectedProvider = AppState.Providers[0];
        }
    }

    private void ResetConversation()
    {
        _conversation = null;
        SelectedConversation = null;
        Messages.Clear();
    }


    [RelayCommand(CanExecute = nameof(CanSend))]
    private async Task SendAsync()
    {
        if (SelectedProvider is null)
        {
            StatusMessage = T("SelectProviderFirst");
            return;
        }

        var text = Draft.Trim();
        if (text.Length == 0) return;

        var selectedProvider = SelectedProvider with { ModelId = SelectedModelId ?? SelectedProvider.ModelId };
        _conversation ??= await _conversations.CreateAsync(selectedProvider, SelectedSkill);
        if (AppState.Conversations.All(item => item.Id != _conversation.Id))
            AppState.Conversations.Insert(0, _conversation);

        Draft = string.Empty;
        var assistant = new ChatMessageViewModel(MessageRole.Assistant, string.Empty);
        Messages.Add(new ChatMessageViewModel(MessageRole.User, text));
        Messages.Add(assistant);
        _sendCancellation = new CancellationTokenSource();
        IsSending = true;
        StatusMessage = T("Generating");

        var channel = Channel.CreateBounded<string>(new BoundedChannelOptions(256)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = true
        });

        var producerTask = Task.Run(async () =>
        {
            try
            {
                await foreach (var update in _chat.SendStreamingAsync(_conversation.Id, text, _sendCancellation.Token)
                                   .ConfigureAwait(false))
                    await channel.Writer.WriteAsync(update, _sendCancellation.Token).ConfigureAwait(false);
                channel.Writer.TryComplete();
            }
            catch (OperationCanceledException)
            {
                channel.Writer.TryComplete();
            }
            catch (Exception ex)
            {
                channel.Writer.TryComplete(ex);
            }
        }, _sendCancellation.Token);

        try
        {
            var pendingContent = new StringBuilder();
            var lastRenderAt = Environment.TickCount64;
            await foreach (var update in channel.Reader.ReadAllAsync(_sendCancellation.Token))
            {
                pendingContent.Append(update);
                var now = Environment.TickCount64;
                if (now - lastRenderAt >= 50)
                {
                    assistant.Content += pendingContent.ToString();
                    pendingContent.Clear();
                    lastRenderAt = now;
                }
            }

            if (pendingContent.Length > 0) assistant.Content += pendingContent.ToString();
            await producerTask;
            StatusMessage = T("Completed");
            await RefreshConversationHistoryAsync();
        }
        catch (OperationCanceledException exception)
        {
            ExceptionLogger.Log(exception, nameof(ChatWindowViewModel), "Chat request cancelled", LogLevel.Information);
            StatusMessage = T("Stopped");
            await RefreshConversationHistoryAsync();
        }
        catch (Exception exception)
        {
            ExceptionLogger.Log(exception, nameof(ChatWindowViewModel), "Chat request failed");
            assistant.Content =
                string.IsNullOrEmpty(assistant.Content) ? "Cannot complete request." : assistant.Content;
            StatusMessage = exception.Message;
            await RefreshConversationHistoryAsync();
        }
        finally
        {
            _sendCancellation.Dispose();
            _sendCancellation = null;
            IsSending = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanStop))]
    private void Stop() => _sendCancellation?.Cancel();

    [RelayCommand(CanExecute = nameof(CanSendOrStop))]
    private void SendOrStop()
    {
        if (IsSending)
        {
            Stop();
            return;
        }

        _ = SendAsync();
    }

    private bool CanSend() => !IsSending && !string.IsNullOrWhiteSpace(Draft);
    private bool CanStop() => IsSending;
    private bool CanSendOrStop() => IsSending || !string.IsNullOrWhiteSpace(Draft);

    // ── Navigation ───────────────────────────────────────────────────────

    [RelayCommand]
    private void OpenSettings() => _navigation?.ShowSettings();

    [RelayCommand]
    private void OpenSkillWorkbench() => _navigation?.ShowSkillSettings();

    [RelayCommand]
    private void ShowChat() => _chatWindowManager?.Show();

    // ── Shortcut helpers ─────────────────────────────────────────────────

    public bool IsChatShortcut(Key key, KeyModifiers modifiers) =>
        ShortcutFormatter.Matches(ChatShortcut, key, modifiers);

    public void UpdateGlobalShortcuts() =>
        _globalHotKeys?.UpdateShortcuts(ChatShortcut, NewChatWindowShortcut);

    partial void OnChatShortcutChanged(string value) => UpdateGlobalShortcuts();

    partial void OnNewChatWindowShortcutChanged(string value) => UpdateGlobalShortcuts();

    private void OnAppStatePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(AppState.ChatShortcut):
                ChatShortcut = AppState.ChatShortcut;
                break;
            case nameof(AppState.NewChatWindowShortcut):
                NewChatWindowShortcut = AppState.NewChatWindowShortcut;
                break;
        }
    }

    // ── Conversation lifecycle ───────────────────────────────────────────

    public async Task OpenConversationAsync(string conversationId, CancellationToken cancellationToken = default)
    {
        var conversation = await _conversationRepository.GetByIdAsync(conversationId, cancellationToken)
            .ConfigureAwait(false);
        if (conversation is null) return;
        if (AppState.Conversations.All(item => item.Id != conversation.Id))
            AppState.Conversations.Insert(0, conversation);
        SelectedConversation = conversation;
    }

    private async Task RefreshConversationHistoryAsync(CancellationToken cancellationToken = default)
    {
        var selectedId = SelectedConversation?.Id;
        await AppState.RefreshConversationHistoryAsync(cancellationToken);
        if (selectedId is not null)
            SelectedConversation = AppState.Conversations.FirstOrDefault(c => c.Id == selectedId);
    }

    private async Task OpenConversationAsync(Conversation conversation)
    {
        _openingConversation = true;
        try
        {
            _conversation = conversation;
            var provider = AppState.Providers.FirstOrDefault(item => item.Id == conversation.ProviderId);
            if (provider is not null) SelectedProvider = provider;

            var skill = AppState.Skills.FirstOrDefault(item => item.Id == conversation.SkillId);
            if (skill is not null) SelectedSkill = skill;

            using var snapshot = JsonDocument.Parse(conversation.ProviderConfiguration);
            if (snapshot.RootElement.TryGetProperty("ModelId", out var modelId))
                SelectedModelId = modelId.GetString();

            Messages.Clear();
            foreach (var message in await _messageRepository.GetByConversationAsync(conversation.Id)
                         .ConfigureAwait(false))
                Messages.Add(new ChatMessageViewModel(message.Role, message.Content));

            StatusMessage = $"Opened: {conversation.Title}";
        }
        finally
        {
            _openingConversation = false;
        }
    }

    // ── Partial change handlers ──────────────────────────────────────────

    partial void OnIsSendingChanged(bool value)
    {
        OnPropertyChanged(nameof(SendButtonText));
        OnPropertyChanged(nameof(SendButtonIcon));
        StopCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedProviderChanged(ApiProvider? value)
    {
        OnPropertyChanged(nameof(AvailableModels));
        OnPropertyChanged(nameof(SelectedProviderDisplayName));
        OnPropertyChanged(nameof(SelectedModelDisplayName));
        if (!_openingConversation) ResetConversation();
        if (value is not null) SelectedModelId = value.ModelId;
    }

    partial void OnSelectedModelIdChanged(string? value)
    {
        OnPropertyChanged(nameof(SelectedModelDisplayName));
        if (!_openingConversation && !string.IsNullOrWhiteSpace(value)) ResetConversation();
    }

    partial void OnSelectedSkillChanged(Skill? value)
    {
        // Changing the selected skill (including when the Skills list is reloaded
        // from the settings window) must NOT reset the active conversation — only
        // the dropdown selection is affected.
        if (value is not null)
        {
            _lastSelectedSkillId = value.Id;
            return;
        }

        // The selection was dropped, e.g. because the previously selected skill
        // was removed during a Skills reload. Restore it, or fall back to first.
        RestoreSkillSelection();
    }

    /// <summary>
    /// Re-validates <see cref="SelectedSkill"/> after the shared <see cref="AppState.Skills"/>
    /// list changes. The current conversation is left untouched; only the dropdown
    /// selection is repaired — preferring the previously selected skill and falling
    /// back to the first available skill if it was deleted.
    /// </summary>
    private void OnSkillsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // The skills list is mutated from a worker thread during a reload, so
        // repair the selection back on the UI thread (idempotent + re-runs on
        // the final state after the last collection change).
        Dispatcher.UIThread.Post(RestoreSkillSelection);
    }

    private void RestoreSkillSelection()
    {
        if (AppState.Skills.Count == 0)
        {
            return;
        }

        var next = _lastSelectedSkillId is null
            ? AppState.Skills[0]
            : AppState.Skills.FirstOrDefault(skill => skill.Id == _lastSelectedSkillId) ?? AppState.Skills[0];

        if (SelectedSkill?.Id != next.Id)
        {
            SelectedSkill = next;
        }
    }

    partial void OnSelectedConversationChanged(Conversation? value)
    {
        if (value is not null) _ = OpenConversationAsync(value);
    }

    public void SelectModel(ApiProvider provider, string model)
    {
        SelectedProvider = provider;
        SelectedModelId = model;
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private string T(string key) => _localization[key];

    public override void Dispose()
    {
        AppState.Skills.CollectionChanged -= OnSkillsCollectionChanged;
        AppState.PropertyChanged -= OnAppStatePropertyChanged;
        _sendCancellation?.Cancel();
        _sendCancellation?.Dispose();
        _sendCancellation = null;

        Messages.Clear();

        _conversation = null;
        _openingConversation = false;
        base.Dispose();
    }
}