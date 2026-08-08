using System.Collections.ObjectModel;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Avalonia.Input;
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
/// Presentation model for the chat window and settings window navigation coordination.
/// </summary>
/// <remarks>
/// Each top-level window receives its own instance. <see cref="AppState"/> is the only shared state so preferences
/// and update progress remain consistent across windows without sharing transient conversation state.
/// Settings pages now have their own dedicated ViewModels (e.g. <see cref="ApiKeySettingsViewModel"/>).
/// </remarks>
public sealed partial class MainWindowViewModel : ViewModelBase
{
    /// <summary>Stable page identifiers used by navigation and settings deep links.</summary>
    public const string GeneralSettingsPage = "general";
    public const string ApiKeySettingsPage = "api-key";
    public const string SkillsSettingsPage = "skills";
    public const string ShortcutSettingsPage = "shortcut";
    public const string HistorySettingsPage = "history";
    public const string AboutSettingsPage = "about";

    private readonly ProviderService _providerService;
    private readonly SkillRepository _skills;
    private readonly ConversationService _conversations;
    private readonly ChatService _chat;
    private readonly ConversationRepository _conversationRepository;
    private readonly MessageRepository _messageRepository;
    private readonly IWindowNavigationService? _navigation;
    private readonly LocalizationService _localization;
    private readonly IGlobalHotKeyService? _globalHotKeys;
    private readonly AppState _state;
    private Conversation? _conversation;
    private CancellationTokenSource? _sendCancellation;
    private bool _openingConversation;

    public MainWindowViewModel(
        ProviderService providerService,
        SkillRepository skills,
        ConversationService conversations,
        ChatService chat,
        ConversationRepository conversationRepository,
        MessageRepository messageRepository,
        IWindowNavigationService? navigation = null,
        IGlobalHotKeyService? globalHotKeys = null,
        LocalizationService? localization = null,
        AppState? appState = null)
    {
        _providerService = providerService;
        _skills = skills;
        _conversations = conversations;
        _chat = chat;
        _conversationRepository = conversationRepository;
        _messageRepository = messageRepository;
        _navigation = navigation;
        _localization = localization ?? new LocalizationService();
        _globalHotKeys = globalHotKeys;
        _state = appState ?? new AppState();
    }

    // ── Collections for chat UI ──────────────────────────────────────────

    public ObservableCollection<ApiProvider> Providers { get; } = [];
    public ObservableCollection<ProviderModelMenuItem> ProviderModelMenuItems { get; } = [];
    public ObservableCollection<Skill> Skills { get; } = [];
    public ObservableCollection<ChatMessageViewModel> Messages { get; } = [];
    public ObservableCollection<Conversation> Conversations { get; } = [];

    // ── Chat-bound selections ────────────────────────────────────────────

    [ObservableProperty]
    private ApiProvider? _selectedProvider;

    [ObservableProperty]
    private string? _selectedModelId;

    [ObservableProperty]
    private Conversation? _selectedConversation;

    [ObservableProperty]
    private Skill? _selectedSkill;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    [NotifyCanExecuteChangedFor(nameof(SendOrStopCommand))]
    private string _draft = string.Empty;

    [ObservableProperty]
    private string _statusMessage = "Loading...";

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

    [ObservableProperty]
    private string _chatShortcut = AppSettingsService.DefaultChatShortcut;

    [ObservableProperty]
    private string _newChatWindowShortcut = AppSettingsService.DefaultNewChatWindowShortcut;

    // ── Settings navigation ──────────────────────────────────────────────

    [ObservableProperty]
    private string _selectedSettingsPageKey = GeneralSettingsPage;

    public void SelectSettingsPage(string pageKey)
    {
        if (string.IsNullOrWhiteSpace(pageKey)) return;
        SelectedSettingsPageKey = pageKey;
    }

    // ── Initialisation ───────────────────────────────────────────────────

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        // Ensure AppState has been seeded once (the first window to load does this).
        if (!_state.SettingsLoaded)
        {
            var settings = new AppSettingsService(
                new AppSettingRepository(new SqliteDatabase(AppPaths.CreateDefault().DatabasePath)));
            _state.ThemeKey = await settings.GetAsync(AppSettingsService.ThemeKey, cancellationToken).ConfigureAwait(false)
                ?? AppSettingsService.DefaultTheme;
            _state.LanguageKey = await settings.GetAsync(AppSettingsService.LanguageKey, cancellationToken).ConfigureAwait(false)
                ?? AppSettingsService.DefaultLanguage;
            _state.ChatShortcut = await settings.GetAsync(AppSettingsService.ChatShortcutKey, cancellationToken).ConfigureAwait(false)
                ?? AppSettingsService.DefaultChatShortcut;
            _state.NewChatWindowShortcut = await settings.GetAsync(AppSettingsService.NewChatWindowShortcutKey, cancellationToken).ConfigureAwait(false)
                ?? AppSettingsService.DefaultNewChatWindowShortcut;
            _state.SettingsLoaded = true;
        }

        ChatShortcut = _state.ChatShortcut;
        NewChatWindowShortcut = _state.NewChatWindowShortcut;

        Providers.Clear();
        ProviderModelMenuItems.Clear();
        foreach (var provider in await _providerService.GetAllAsync(cancellationToken).ConfigureAwait(false))
        {
            if (provider.IsEnabled)
            {
                Providers.Add(provider);
                ProviderModelMenuItems.Add(new ProviderModelMenuItem(
                    provider,
                    provider.ModelIds
                        .Where(model => !string.IsNullOrWhiteSpace(model))
                        .Select(model => new ModelMenuItem(model, new RelayCommand(() => SelectModel(provider, model))))
                        .ToArray()));
            }
        }

        Skills.Clear();
        foreach (var skill in await _skills.GetEnabledAsync(cancellationToken).ConfigureAwait(false))
            Skills.Add(skill);

        SelectedProvider = Providers.FirstOrDefault(p => p.IsDefault) ?? Providers.FirstOrDefault();
        SelectedSkill = Skills.FirstOrDefault();

        Conversations.Clear();
        foreach (var conversation in await _conversationRepository.GetRecentAsync(cancellationToken).ConfigureAwait(false))
            Conversations.Add(conversation);

        StatusMessage = SelectedProvider is null ? T("NoProvider") : T("Ready");
    }

    // ── Chat actions ─────────────────────────────────────────────────────

    [RelayCommand]
    private void NewConversation()
    {
        _conversation = null;
        SelectedConversation = null;
        Messages.Clear();
        SelectedSkill = Skills.FirstOrDefault();
        StatusMessage = T("NewConversationStatus");
    }

    private void ResetConversation()
    {
        _conversation = null;
        SelectedConversation = null;
        Messages.Clear();
    }

    private void SelectModel(ApiProvider provider, string model)
    {
        SelectedProvider = provider;
        SelectedModelId = model;
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
        if (!Conversations.Any(item => item.Id == _conversation.Id))
            Conversations.Insert(0, _conversation);

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
                await foreach (var update in _chat.SendStreamingAsync(_conversation.Id, text, _sendCancellation.Token).ConfigureAwait(false))
                    await channel.Writer.WriteAsync(update, _sendCancellation.Token).ConfigureAwait(false);
                channel.Writer.TryComplete();
            }
            catch (OperationCanceledException) { channel.Writer.TryComplete(); }
            catch (Exception ex) { channel.Writer.TryComplete(ex); }
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
            ExceptionLogger.Log(exception, nameof(MainWindowViewModel), "Chat request cancelled", LogLevel.Information);
            StatusMessage = T("Stopped");
            await RefreshConversationHistoryAsync();
        }
        catch (Exception exception)
        {
            ExceptionLogger.Log(exception, nameof(MainWindowViewModel), "Chat request failed");
            assistant.Content = string.IsNullOrEmpty(assistant.Content) ? "Cannot complete request." : assistant.Content;
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
        if (IsSending) { Stop(); return; }
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
    private void ShowChat() => _navigation?.ShowChat();

    // ── Shortcut helpers ─────────────────────────────────────────────────

    public bool IsChatShortcut(Key key, KeyModifiers modifiers) =>
        ShortcutFormatter.Matches(ChatShortcut, key, modifiers);

    public void UpdateGlobalShortcuts() =>
        _globalHotKeys?.UpdateShortcuts(ChatShortcut, NewChatWindowShortcut);

    // ── Conversation lifecycle ───────────────────────────────────────────

    public async Task OpenConversationAsync(string conversationId, CancellationToken cancellationToken = default)
    {
        var conversation = await _conversationRepository.GetByIdAsync(conversationId, cancellationToken).ConfigureAwait(false);
        if (conversation is null) return;
        if (!Conversations.Any(item => item.Id == conversation.Id))
            Conversations.Insert(0, conversation);
        SelectedConversation = conversation;
    }

    private async Task RefreshConversationHistoryAsync(CancellationToken cancellationToken = default)
    {
        var selectedId = SelectedConversation?.Id;
        var conversations = await _conversationRepository.GetRecentAsync(cancellationToken).ConfigureAwait(false);
        Conversations.Clear();
        foreach (var c in conversations) Conversations.Add(c);
        if (selectedId is not null)
            SelectedConversation = Conversations.FirstOrDefault(c => c.Id == selectedId);
    }

    private async Task OpenConversationAsync(Conversation conversation)
    {
        _openingConversation = true;
        try
        {
            _conversation = conversation;
            var provider = Providers.FirstOrDefault(item => item.Id == conversation.ProviderId);
            if (provider is not null) SelectedProvider = provider;

            var skill = Skills.FirstOrDefault(item => item.Id == conversation.SkillId);
            if (skill is not null) SelectedSkill = skill;

            using var snapshot = JsonDocument.Parse(conversation.ProviderConfiguration);
            if (snapshot.RootElement.TryGetProperty("ModelId", out var modelId))
                SelectedModelId = modelId.GetString();

            Messages.Clear();
            foreach (var message in await _messageRepository.GetByConversationAsync(conversation.Id).ConfigureAwait(false))
                Messages.Add(new ChatMessageViewModel(message.Role, message.Content));

            StatusMessage = $"Opened: {conversation.Title}";
        }
        finally { _openingConversation = false; }
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
        if (!_openingConversation) ResetConversation();
    }

    partial void OnSelectedConversationChanged(Conversation? value)
    {
        if (value is not null) _ = OpenConversationAsync(value);
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private string T(string key) => _localization[key];

    public void Dispose()
    {
        _sendCancellation?.Cancel();
        _sendCancellation?.Dispose();
        _sendCancellation = null;
    }
}
