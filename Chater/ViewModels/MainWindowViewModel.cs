using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text.Json;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Chater.Data;
using Chater.Models;
using Chater.Models.Enums;
using Chater.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Material.Icons;

namespace Chater.ViewModels;

/// <summary>
/// Shared presentation model for a chat window and the settings window.
/// </summary>
/// <remarks>
/// Each top-level window receives its own instance. <see cref="AppState"/> is the only shared state so preferences
/// and update progress remain consistent across windows without sharing transient conversation state.
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
    private readonly SkillService _skillService;
    private readonly IWindowNavigationService? _navigation;
    private readonly AppSettingsService _settings;
    private readonly LocalizationService _localization;
    private readonly IGlobalHotKeyService? _globalHotKeys;
    private readonly IStartupService _startup;
    private readonly IConfirmationService? _confirmation;
    private readonly AppState _state;
    // This is the transient active chat. It is intentionally not shared through AppState.
    private Conversation? _conversation;
    // Cancels only the current streaming request; it must be disposed before a subsequent send starts.
    private CancellationTokenSource? _sendCancellation;
    // Guards selection side effects while a persisted conversation is restoring its provider and skill.
    private bool _openingConversation;
    private bool _loadingSettings;
    private bool _restoringStartupSetting;
    // Paging state prevents duplicate loads when the history ScrollViewer reports multiple extent changes.
    private int _historyPage;
    private bool _historyHasMore = true;
    private bool _historyLoading;
    private bool _syncingAppState;
    private const int HistoryPageSize = 20;

    public MainWindowViewModel(ProviderService providerService, SkillRepository skills, ConversationService conversations, ChatService chat, ConversationRepository conversationRepository, MessageRepository messageRepository, SkillService skillService, AppSettingsService settings, IWindowNavigationService? navigation = null, IGlobalHotKeyService? globalHotKeys = null, LocalizationService? localization = null, IStartupService? startup = null, AppState? appState = null, IConfirmationService? confirmation = null)
    {
        _providerService = providerService;
        _skills = skills;
        _conversations = conversations;
        _chat = chat;
        _conversationRepository = conversationRepository;
        _messageRepository = messageRepository;
        _skillService = skillService;
        _navigation = navigation;
        _settings = settings;
        _localization = localization ?? new LocalizationService();
        _globalHotKeys = globalHotKeys;
        _startup = startup ?? new StartupService();
        _state = appState ?? new AppState();
        _confirmation = confirmation;
        _state.PropertyChanged += OnAppStatePropertyChanged;
        ApplyUpdateProgress(_state.UpdateProgress);
        RefreshThemeOptions();
    }

    /// <summary>Collections bound to the chat and settings views.</summary>
    public ObservableCollection<ApiProvider> Providers { get; } = [];
    public ObservableCollection<ProviderModelMenuItem> ProviderModelMenuItems { get; } = [];
    public ObservableCollection<Skill> Skills { get; } = [];
    public ObservableCollection<ChatMessageViewModel> Messages { get; } = [];
    public ObservableCollection<Conversation> Conversations { get; } = [];
    public ObservableCollection<Conversation> HistoryConversations { get; } = [];
    public ObservableCollection<ChatMessageViewModel> HistoryMessages { get; } = [];
    public IReadOnlyList<ProviderType> ProviderTypes { get; } = Enum.GetValues<ProviderType>();

    /// <summary>Models offered by the provider selected in the current window.</summary>
    public IReadOnlyList<string> AvailableModels => SelectedProvider?.ModelIds ?? [];
    public LocalizationService Localization => _localization;
    public AppState State => _state;
    public string CurrentVersion => _state.CurrentVersion;
    public IReadOnlyList<LanguageOption> LanguageOptions => _localization.LanguageOptions;
    public string SelectedProviderDisplayName => SelectedProvider?.Name ?? T("SelectApiKey");
    public string SelectedModelDisplayName => SelectedModelId ?? SelectedProvider?.ModelId ?? T("ModelPlaceholder");
    public ObservableCollection<ThemeOption> ThemeOptions { get; } = [];
    public IReadOnlyList<AccentColorOption> AccentColorOptions { get; } =
    [
        new("sky", "Sky", "#0EA5E9"),
        new("indigo", "Indigo", "#6366F1"),
        new("violet", "Violet", "#8B5CF6"),
        new("teal", "Teal", "#14B8A6"),
        new("green", "Green", "#22C55E"),
        new("orange", "Orange", "#F97316"),
        new("rose", "Rose", "#F43F5E")
    ];

    [ObservableProperty]
    private ThemeOption? _selectedTheme;

    [ObservableProperty]
    private Color _accentColor = Color.Parse(AppSettingsService.DefaultAccentColor);

    /// <summary>Brush projection of the selected accent color for XAML bindings.</summary>
    public IBrush AccentBrush => new SolidColorBrush(AccentColor);

    [ObservableProperty]
    private LanguageOption? _selectedLanguage;

    [ObservableProperty]
    private string _chatShortcut = AppSettingsService.DefaultChatShortcut;

    [ObservableProperty]
    private string _newChatWindowShortcut = AppSettingsService.DefaultNewChatWindowShortcut;

    [ObservableProperty]
    private bool _launchAtStartup;

    [ObservableProperty]
    private ApiProvider? _selectedProvider;

    [ObservableProperty]
    private string? _selectedModelId;


    [ObservableProperty]
    private Conversation? _selectedConversation;

    [ObservableProperty]
    private Conversation? _selectedHistoryConversation;

    [ObservableProperty]
    private Skill? _selectedSkill;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    [NotifyCanExecuteChangedFor(nameof(SendOrStopCommand))]
    private string _draft = string.Empty;

    [ObservableProperty]
    private string _statusMessage = "正在加载配置…";

    [ObservableProperty]
    private string _updateStatus = string.Empty;

    [ObservableProperty]
    private bool _isDownloadingUpdate;

    [ObservableProperty]
    private double _updateProgress;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    [NotifyCanExecuteChangedFor(nameof(SendOrStopCommand))]
    private bool _isSending;

    /// <summary>Uses one button for send and cancellation to avoid competing actions during a stream.</summary>
    public string SendButtonText => IsSending ? T("Stop") : T("Send");
    public MaterialIconKind SendButtonIcon => IsSending ? MaterialIconKind.Stop : MaterialIconKind.Send;

    [ObservableProperty]
    private string _providerName = string.Empty;

    [ObservableProperty]
    private ProviderType _providerType = ProviderType.OpenAi;

    [ObservableProperty]
    private string _providerModelId = string.Empty;

    [ObservableProperty]
    private string _providerEndpoint = string.Empty;

    [ObservableProperty]
    private string _providerApiKey = string.Empty;

    [ObservableProperty]
    private string _skillName = string.Empty;

    [ObservableProperty]
    private string _skillPrompt = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsApiKeySettingsVisible))]
    [NotifyPropertyChangedFor(nameof(IsSkillSettingsVisible))]
    [NotifyPropertyChangedFor(nameof(IsGeneralSettingsVisible))]
    [NotifyPropertyChangedFor(nameof(IsShortcutSettingsVisible))]
    [NotifyPropertyChangedFor(nameof(IsHistoryVisible))]
    private int _settingsTabIndex;

    [ObservableProperty]
    private string _selectedSettingsPageKey = GeneralSettingsPage;

    public bool IsGeneralSettingsVisible => SettingsTabIndex == 0;
    public bool IsApiKeySettingsVisible => SettingsTabIndex == 1;
    public bool IsSkillSettingsVisible => SettingsTabIndex == 2;
    public bool IsShortcutSettingsVisible => SettingsTabIndex == 3;
    public bool IsHistoryVisible => SettingsTabIndex == 4;

    public void SelectSettingsPage(string pageKey)
    {
        if (string.IsNullOrWhiteSpace(pageKey)) return;
        SelectedSettingsPageKey = pageKey;
    }

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!_state.SettingsLoaded)
        {
            _state.ThemeKey = await _settings.GetAsync(AppSettingsService.ThemeKey, cancellationToken).ConfigureAwait(false) ?? AppSettingsService.DefaultTheme;
            _state.AccentColorHex = await _settings.GetAsync(AppSettingsService.AccentColorKey, cancellationToken).ConfigureAwait(false) ?? AppSettingsService.DefaultAccentColor;
            _state.LanguageKey = await _settings.GetAsync(AppSettingsService.LanguageKey, cancellationToken).ConfigureAwait(false) ?? AppSettingsService.DefaultLanguage;
            _state.ChatShortcut = await _settings.GetAsync(AppSettingsService.ChatShortcutKey, cancellationToken).ConfigureAwait(false) ?? AppSettingsService.DefaultChatShortcut;
            _state.NewChatWindowShortcut = await _settings.GetAsync(AppSettingsService.NewChatWindowShortcutKey, cancellationToken).ConfigureAwait(false) ?? AppSettingsService.DefaultNewChatWindowShortcut;
            _state.LaunchAtStartup = _startup.IsEnabled();
            _state.SettingsLoaded = true;
        }

        var theme = _state.ThemeKey;
        var accentColor = _state.AccentColorHex;
        var language = _state.LanguageKey;
        _loadingSettings = true;
        try
        {
            SelectedLanguage = LanguageOptions.FirstOrDefault(item => item.Key == language) ?? LanguageOptions[0];
            _localization.SetLanguage(SelectedLanguage.Key);
            SelectedTheme = ThemeOptions.FirstOrDefault(item => item.Key == theme) ?? ThemeOptions[0];
            AccentColor = AppSettingsService.TryParseColor(accentColor, out var parsedAccent)
                ? parsedAccent
                : Color.Parse(AppSettingsService.DefaultAccentColor);
            ChatShortcut = _state.ChatShortcut;
            NewChatWindowShortcut = _state.NewChatWindowShortcut;
            LaunchAtStartup = _state.LaunchAtStartup;
            AppSettingsService.ApplyTheme(SelectedTheme.Key);
        }
        finally
        {
            _loadingSettings = false;
        }
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
        {
            Skills.Add(skill);
        }

        SelectedProvider = Providers.FirstOrDefault(provider => provider.IsDefault) ?? Providers.FirstOrDefault();
        SelectedSkill = Skills.FirstOrDefault();
        Conversations.Clear();
        foreach (var conversation in await _conversationRepository.GetRecentAsync(cancellationToken).ConfigureAwait(false))
        {
            Conversations.Add(conversation);
        }
        StatusMessage = SelectedProvider is null ? T("NoProvider") : T("Ready");
    }

    public async Task OpenConversationAsync(string conversationId, CancellationToken cancellationToken = default)
    {
        var conversation = await _conversationRepository.GetByIdAsync(conversationId, cancellationToken).ConfigureAwait(false);
        if (conversation is null) return;
        if (!Conversations.Any(item => item.Id == conversation.Id))
            Conversations.Insert(0, conversation);
        SelectedConversation = conversation;
    }

    public void Dispose()
    {
        _state.PropertyChanged -= OnAppStatePropertyChanged;
        _sendCancellation?.Cancel();
        _sendCancellation?.Dispose();
        _sendCancellation = null;
    }

    [RelayCommand]
    private void NewConversation()
    {
        _conversation = null;
        SelectedConversation = null;
        Messages.Clear();
        StatusMessage = T("NewConversationStatus");
    }

    [RelayCommand]
    private void OpenSettings() => _navigation?.ShowSettings();

    [RelayCommand]
    private void OpenSkillWorkbench() => _navigation?.ShowSkillSettings();

    [RelayCommand]
    private void ShowApiKeySettings()
    {
        SettingsTabIndex = 0;
        SelectSettingsPage(ApiKeySettingsPage);
    }

    [RelayCommand]
    private void ShowSkillSettings()
    {
        SettingsTabIndex = 1;
        SelectSettingsPage(SkillsSettingsPage);
    }

    [RelayCommand]
    private void ShowGeneralSettings()
    {
        SettingsTabIndex = 2;
        SelectSettingsPage(GeneralSettingsPage);
    }

    [RelayCommand]
    private void ShowShortcutSettings()
    {
        SettingsTabIndex = 3;
        SelectSettingsPage(ShortcutSettingsPage);
    }

    [RelayCommand]
    private async Task LoadHistoryAsync()
    {
        if (_historyLoading) return;
        _historyPage = 0;
        _historyHasMore = true;
        HistoryConversations.Clear();
        HistoryMessages.Clear();
        SelectedHistoryConversation = null;
        await LoadMoreHistoryAsync().ConfigureAwait(false);
    }

    [RelayCommand]
    private async Task LoadMoreHistoryAsync()
    {
        if (_historyLoading || !_historyHasMore) return;
        _historyLoading = true;
        try
        {
            var page = await _conversationRepository.GetPageAsync(_historyPage, HistoryPageSize).ConfigureAwait(false);
            foreach (var conversation in page) HistoryConversations.Add(conversation);
            _historyPage++;
            _historyHasMore = page.Count == HistoryPageSize;
        }
        finally { _historyLoading = false; }
    }

    [RelayCommand]
    private void StartHistoryConversation()
    {
        if (SelectedHistoryConversation is not null)
            _navigation?.ShowChat(SelectedHistoryConversation.Id);
    }

    [RelayCommand(CanExecute = nameof(CanDeleteHistoryConversation))]
    private async Task DeleteHistoryConversationAsync()
    {
        var conversation = SelectedHistoryConversation;
        if (conversation is null)
        {
            return;
        }

        if (_confirmation is not null && !await _confirmation.ConfirmDeleteAsync(conversation.Title))
        {
            return;
        }

        await _conversationRepository.ArchiveAsync(conversation.Id).ConfigureAwait(false);
        await LoadHistoryAsync().ConfigureAwait(false);
        StatusMessage = T("HistoryDeleted");
    }

    private bool CanDeleteHistoryConversation() => SelectedHistoryConversation is not null;

    [RelayCommand]
    private async Task SaveGeneralSettingsAsync()
    {
        var theme = SelectedTheme?.Key ?? AppSettingsService.DefaultTheme;
        await _settings.SaveAsync(AppSettingsService.ThemeKey, theme).ConfigureAwait(false);
        AppSettingsService.ApplyTheme(theme);
        StatusMessage = T("Ready");
    }

    [RelayCommand]
    private async Task SaveShortcutSettingsAsync()
    {
        if (!IsShortcutOrEmpty(ChatShortcut) || !IsShortcutOrEmpty(NewChatWindowShortcut))
        {
            StatusMessage = T("ShortcutPlaceholder");
            return;
        }
        await _settings.SaveAsync(AppSettingsService.ChatShortcutKey, ChatShortcut).ConfigureAwait(false);
        await _settings.SaveAsync(AppSettingsService.NewChatWindowShortcutKey, NewChatWindowShortcut).ConfigureAwait(false);
        UpdateGlobalShortcuts();
        StatusMessage = T("Ready");
    }

    [RelayCommand]
    private async Task ClearShortcutAsync()
    {
        if (_confirmation is not null && !await _confirmation.ConfirmDeleteAsync(T("ChatShortcut")))
        {
            return;
        }

        ChatShortcut = string.Empty;
        await _settings.SaveAsync(AppSettingsService.ChatShortcutKey, string.Empty).ConfigureAwait(false);
        UpdateGlobalShortcuts();
        StatusMessage = T("ShortcutCleared");
    }

    [RelayCommand]
    private async Task ClearNewChatWindowShortcutAsync()
    {
        if (_confirmation is not null && !await _confirmation.ConfirmDeleteAsync(T("NewChatWindowShortcut")))
        {
            return;
        }

        NewChatWindowShortcut = string.Empty;
        await _settings.SaveAsync(AppSettingsService.NewChatWindowShortcutKey, string.Empty).ConfigureAwait(false);
        UpdateGlobalShortcuts();
        StatusMessage = T("ShortcutCleared");
    }

    [RelayCommand]
    private void ShowChat() => _navigation?.ShowChat();

    public bool IsChatShortcut(Key key, KeyModifiers modifiers) => ShortcutFormatter.Matches(ChatShortcut, key, modifiers);

    private void OnAppStatePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            _syncingAppState = true;
            try
            {
                switch (e.PropertyName)
                {
                    case nameof(AppState.UpdateProgress):
                        ApplyUpdateProgress(_state.UpdateProgress);
                        break;
                    case nameof(AppState.ThemeKey):
                        SelectedTheme = ThemeOptions.FirstOrDefault(item => item.Key == _state.ThemeKey);
                        break;
                    case nameof(AppState.AccentColorHex):
                        if (AppSettingsService.TryParseColor(_state.AccentColorHex, out var accent))
                            AccentColor = accent;
                        break;
                    case nameof(AppState.LanguageKey):
                        SelectedLanguage = LanguageOptions.FirstOrDefault(item => item.Key == _state.LanguageKey);
                        break;
                    case nameof(AppState.ChatShortcut):
                        ChatShortcut = _state.ChatShortcut;
                        break;
                    case nameof(AppState.NewChatWindowShortcut):
                        NewChatWindowShortcut = _state.NewChatWindowShortcut;
                        break;
                    case nameof(AppState.LaunchAtStartup):
                        LaunchAtStartup = _state.LaunchAtStartup;
                        break;
                }
            }
            finally
            {
                _syncingAppState = false;
            }
        });
    }

    private void ApplyUpdateProgress(UpdateProgress progress)
    {
        UpdateProgress = progress.Progress ?? (progress.State == UpdateState.Ready ? 1 : 0);
        IsDownloadingUpdate = progress.State == UpdateState.Downloading;
        UpdateStatus = progress.State switch
        {
            UpdateState.Checking => T("CheckingForUpdates"),
            UpdateState.Available => T("UpdateAvailable"),
            UpdateState.Downloading => T("DownloadingUpdate"),
            UpdateState.Ready => T("UpdateReady"),
            UpdateState.UpToDate => T("NoUpdates"),
            UpdateState.Failed => string.Format(T("UpdateCheckFailed"), progress.ErrorMessage ?? string.Empty),
            _ => string.Empty
        };
    }

    private string T(string key) => _localization[key];

    private void RefreshThemeOptions()
    {
        ThemeOptions.Clear();
        ThemeOptions.Add(new ThemeOption("system", T("System")));
        ThemeOptions.Add(new ThemeOption("light", T("Light")));
        ThemeOptions.Add(new ThemeOption("dark", T("Dark")));
    }

    private void SelectModel(ApiProvider provider, string model)
    {
        SelectedProvider = provider;
        SelectedModelId = model;
    }

    [RelayCommand]
    private void AddProvider()
    {
        SelectedProvider = null;
        ProviderName = string.Empty;
        ProviderType = ProviderType.OpenAi;
        ProviderModelId = string.Empty;
        SelectedModelId = null;
        ProviderEndpoint = string.Empty;
        ProviderApiKey = string.Empty;
        StatusMessage = T("AddingProvider");
    }

    [RelayCommand]
    private async Task SaveProviderAsync()
    {
        var provider = BuildEditedProvider();
        try
        {
            await _providerService.SaveAsync(provider).ConfigureAwait(false);
            await LoadAsync().ConfigureAwait(false);
            SelectedProvider = Providers.FirstOrDefault(item => item.Id == provider.Id);
            StatusMessage = T("ProviderSaved");
        }
        catch (Exception exception)
        {
            StatusMessage = exception.Message;
        }
    }

    [RelayCommand]
    private async Task DeleteProviderAsync(ApiProvider? provider)
    {
        if (provider is null)
        {
            return;
        }

        if (_confirmation is not null && !await _confirmation.ConfirmDeleteAsync(provider.Name))
        {
            return;
        }

        try
        {
            await _providerService.DeleteAsync(provider.Id).ConfigureAwait(false);
            await LoadAsync().ConfigureAwait(false);
            StatusMessage = T("ProviderDeleted");
        }
        catch (Exception exception)
        {
            StatusMessage = exception.Message;
        }
    }

    [RelayCommand]
    private async Task TestProviderConnectionAsync()
    {
        try
        {
            StatusMessage = T("Testing");
            var result = await _providerService.TestConnectionAsync(BuildEditedProvider()).ConfigureAwait(false);
            StatusMessage = result.Message;
        }
        catch (Exception exception)
        {
            StatusMessage = exception.Message;
        }
    }

    [RelayCommand]
    private void AddSkill()
    {
        SelectedSkill = null;
        SkillName = string.Empty;
        SkillPrompt = string.Empty;
        StatusMessage = T("AddingSkill");
    }

    [RelayCommand]
    private async Task SaveSkillAsync()
    {
        var existing = SelectedSkill;
        var now = DateTimeOffset.UtcNow;
        try
        {
            var saved = await _skillService.SaveCustomAsync(new Skill(existing?.Id ?? Guid.NewGuid().ToString("N"), SkillName, null, SkillPrompt, null, false, true, existing?.SortOrder ?? Skills.Count + 100, existing?.Version ?? 0, existing?.CreatedAt ?? now, now)).ConfigureAwait(false);
            await ReloadSkillsAsync().ConfigureAwait(false);
            SelectedSkill = Skills.FirstOrDefault(item => item.Id == saved.Id);
            StatusMessage = T("SkillSaved");
        }
        catch (Exception exception)
        {
            StatusMessage = exception.Message;
        }
    }

    [RelayCommand]
    private async Task DeleteSkillAsync(Skill? skill)
    {
        if (skill is null)
        {
            return;
        }

        if (_confirmation is not null && !await _confirmation.ConfirmDeleteAsync(skill.Name))
        {
            return;
        }

        try
        {
            await _skillService.DeleteCustomAsync(skill.Id).ConfigureAwait(false);
            await ReloadSkillsAsync().ConfigureAwait(false);
            SelectedSkill = Skills.FirstOrDefault();
            StatusMessage = T("SkillDeleted");
        }
        catch (Exception exception)
        {
            StatusMessage = exception.Message;
        }
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
        if (text.Length == 0)
        {
            return;
        }

        var selectedProvider = SelectedProvider with { ModelId = SelectedModelId ?? SelectedProvider.ModelId };
        _conversation ??= await _conversations.CreateAsync(selectedProvider, SelectedSkill).ConfigureAwait(false);
        if (!Conversations.Any(item => item.Id == _conversation.Id))
        {
            Conversations.Insert(0, _conversation);
        }
        Draft = string.Empty;
        var assistant = new ChatMessageViewModel(MessageRole.Assistant, string.Empty);
        Messages.Add(new ChatMessageViewModel(MessageRole.User, text));
        Messages.Add(assistant);
        _sendCancellation = new CancellationTokenSource();
        IsSending = true;
        StatusMessage = T("Generating");
        try
        {
            await foreach (var update in _chat.SendStreamingAsync(_conversation.Id, text, _sendCancellation.Token).ConfigureAwait(false))
            {
                assistant.Content += update;
            }

            StatusMessage = T("Completed");
            await RefreshConversationHistoryAsync().ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            StatusMessage = T("Stopped");
            await RefreshConversationHistoryAsync().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            assistant.Content = string.IsNullOrEmpty(assistant.Content) ? "无法完成请求。" : assistant.Content;
            StatusMessage = exception.Message;
            await RefreshConversationHistoryAsync().ConfigureAwait(false);
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

    private ApiProvider BuildEditedProvider()
    {
        var existing = SelectedProvider;
        var now = DateTimeOffset.UtcNow;
        var modelIds = ProviderModelId
            .Split(['\r', '\n', ',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var activeModel = SelectedModelId is not null && modelIds.Contains(SelectedModelId, StringComparer.OrdinalIgnoreCase)
            ? SelectedModelId
            : modelIds.FirstOrDefault() ?? string.Empty;
        return new ApiProvider(
            existing?.Id ?? Guid.NewGuid().ToString("N"),
            ProviderName.Trim(),
            ProviderType,
            string.IsNullOrWhiteSpace(ProviderApiKey) ? existing?.ApiKey ?? string.Empty : ProviderApiKey,
            string.IsNullOrWhiteSpace(ProviderEndpoint) ? null : ProviderEndpoint.Trim(),
            activeModel,
            existing?.IsDefault ?? Providers.Count == 0,
            true,
            existing?.CreatedAt ?? now,
            now) with { ModelIds = modelIds };
    }

    private async Task ReloadSkillsAsync(CancellationToken cancellationToken = default)
    {
        Skills.Clear();
        foreach (var skill in await _skills.GetEnabledAsync(cancellationToken).ConfigureAwait(false))
        {
            Skills.Add(skill);
        }
    }

    private async Task RefreshConversationHistoryAsync(CancellationToken cancellationToken = default)
    {
        var selectedId = SelectedConversation?.Id;
        var conversations = await _conversationRepository.GetRecentAsync(cancellationToken).ConfigureAwait(false);
        Conversations.Clear();
        foreach (var conversation in conversations)
        {
            Conversations.Add(conversation);
        }

        if (selectedId is not null)
        {
            SelectedConversation = Conversations.FirstOrDefault(conversation => conversation.Id == selectedId);
        }
    }

    partial void OnIsSendingChanged(bool value)
    {
        OnPropertyChanged(nameof(SendButtonText));
        OnPropertyChanged(nameof(SendButtonIcon));
        StopCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedLanguageChanged(LanguageOption? value)
    {
        if (value is null)
        {
            return;
        }

        _localization.SetLanguage(value.Key);
        OnPropertyChanged(nameof(SelectedProviderDisplayName));
        OnPropertyChanged(nameof(SelectedModelDisplayName));
        OnPropertyChanged(nameof(SendButtonText));
        var selectedThemeKey = SelectedTheme?.Key;
        RefreshThemeOptions();
        if (selectedThemeKey is not null)
        {
            SelectedTheme = ThemeOptions.First(item => item.Key == selectedThemeKey);
        }
        if (!_loadingSettings)
        {
            if (!_syncingAppState)
            {
                _state.LanguageKey = value.Key;
                _ = PersistLanguageAsync(value.Key);
                StatusMessage = T("Ready");
            }
        }
    }

    partial void OnSelectedThemeChanged(ThemeOption? value)
    {
        if (value is null)
        {
            return;
        }

        AppSettingsService.ApplyTheme(value.Key);
        if (!_loadingSettings && !_syncingAppState)
        {
            _state.ThemeKey = value.Key;
            _ = PersistThemeAsync(value.Key);
        }
    }

    partial void OnAccentColorChanged(Color value)
    {
        OnPropertyChanged(nameof(AccentBrush));
        var hex = ToHex(value);
        AppSettingsService.ApplyAccentColor(hex);
        if (!_loadingSettings && !_syncingAppState)
        {
            _state.AccentColorHex = hex;
            _ = PersistAccentColorAsync(hex);
        }
    }

    [RelayCommand]
    private void SelectAccentColor(AccentColorOption? option)
    {
        if (option is not null)
        {
            AccentColor = option.Color;
        }
    }

    partial void OnChatShortcutChanged(string value)
    {
        if (_loadingSettings || _syncingAppState || !ShortcutFormatter.TryParse(value, out _))
        {
            return;
        }

        UpdateGlobalShortcuts();
        _ = PersistShortcutAsync(value);
        _state.ChatShortcut = value;
    }

    partial void OnNewChatWindowShortcutChanged(string value)
    {
        if (_loadingSettings || _syncingAppState || !IsShortcutOrEmpty(value))
        {
            return;
        }

        UpdateGlobalShortcuts();
        _ = PersistNewChatWindowShortcutAsync(value);
        _state.NewChatWindowShortcut = value;
    }

    partial void OnLaunchAtStartupChanged(bool value)
    {
        if (_loadingSettings || _syncingAppState || _restoringStartupSetting)
        {
            return;
        }

        _ = PersistLaunchAtStartupAsync(value);
        _state.LaunchAtStartup = value;
    }

    private async Task PersistLaunchAtStartupAsync(bool enabled)
    {
        await Task.Yield();
        if (_startup.TrySetEnabled(enabled))
        {
            StatusMessage = T(enabled ? "StartupEnabled" : "StartupDisabled");
            return;
        }

        _restoringStartupSetting = true;
        try
        {
            LaunchAtStartup = _startup.IsEnabled();
        }
        finally
        {
            _restoringStartupSetting = false;
        }
        StatusMessage = T("StartupPermissionRequired");
        _startup.OpenPermissionSettings();
    }

    private async Task PersistThemeAsync(string theme)
    {
        try
        {
            await _settings.SaveAsync(AppSettingsService.ThemeKey, theme).ConfigureAwait(false);
            StatusMessage = T("Ready");
        }
        catch (Exception exception)
        {
            StatusMessage = exception.Message;
        }
    }

    private async Task PersistAccentColorAsync(string hex)
    {
        try
        {
            await _settings.SaveAsync(AppSettingsService.AccentColorKey, hex).ConfigureAwait(false);
            StatusMessage = T("Ready");
        }
        catch (Exception exception)
        {
            StatusMessage = exception.Message;
        }
    }

    private static string ToHex(Color color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";

    private async Task PersistShortcutAsync(string shortcut)
    {
        try
        {
            await _settings.SaveAsync(AppSettingsService.ChatShortcutKey, shortcut).ConfigureAwait(false);
            StatusMessage = T("Ready");
        }
        catch (Exception exception)
        {
            StatusMessage = exception.Message;
        }
    }

    private void UpdateGlobalShortcuts() => _globalHotKeys?.UpdateShortcuts(ChatShortcut, NewChatWindowShortcut);

    private static bool IsShortcutOrEmpty(string shortcut) =>
        string.IsNullOrEmpty(shortcut) || ShortcutFormatter.TryParse(shortcut, out _);

    private async Task PersistNewChatWindowShortcutAsync(string shortcut)
    {
        try
        {
            await _settings.SaveAsync(AppSettingsService.NewChatWindowShortcutKey, shortcut).ConfigureAwait(false);
            StatusMessage = T("Ready");
        }
        catch (Exception exception)
        {
            StatusMessage = exception.Message;
        }
    }

    private async Task PersistLanguageAsync(string language)
    {
        try
        {
            await _settings.SaveAsync(AppSettingsService.LanguageKey, language).ConfigureAwait(false);
            StatusMessage = T("Ready");
        }
        catch (Exception exception)
        {
            StatusMessage = exception.Message;
        }
    }

    partial void OnSelectedProviderChanged(ApiProvider? value)
    {
        OnPropertyChanged(nameof(AvailableModels));
        OnPropertyChanged(nameof(SelectedProviderDisplayName));
        OnPropertyChanged(nameof(SelectedModelDisplayName));
        if (!_openingConversation)
        {
            NewConversation();
        }
        if (value is null)
        {
            return;
        }

        ProviderName = value.Name;
        ProviderType = value.ProviderType;
        SelectedModelId = value.ModelId;
        ProviderModelId = string.Join(Environment.NewLine, value.ModelIds);
        ProviderEndpoint = value.Endpoint ?? string.Empty;
        ProviderApiKey = string.Empty;
    }

    partial void OnSelectedModelIdChanged(string? value)
    {
        OnPropertyChanged(nameof(SelectedModelDisplayName));
        if (!_openingConversation && !string.IsNullOrWhiteSpace(value))
        {
            NewConversation();
        }
    }

    partial void OnSelectedSkillChanged(Skill? value)
    {
        if (!_openingConversation)
        {
            NewConversation();
        }
        if (value is not null)
        {
            SkillName = value.Name;
            SkillPrompt = value.SystemPrompt;
        }
    }

    partial void OnSelectedConversationChanged(Conversation? value)
    {
        if (value is not null)
        {
            _ = OpenConversationAsync(value);
        }
    }

    partial void OnSelectedHistoryConversationChanged(Conversation? value)
    {
        DeleteHistoryConversationCommand.NotifyCanExecuteChanged();
        if (value is not null) _ = ReloadSelectedHistoryAsync(value.Id);
    }

    private async Task ReloadSelectedHistoryAsync(string conversationId)
    {
        var conversation = await _conversationRepository.GetByIdAsync(conversationId).ConfigureAwait(false);
        if (conversation is null) return;
        SelectedHistoryConversation = conversation;
        HistoryMessages.Clear();
        foreach (var message in await _messageRepository.GetByConversationAsync(conversation.Id).ConfigureAwait(false))
            HistoryMessages.Add(new ChatMessageViewModel(message.Role, message.Content));
    }

    private async Task OpenConversationAsync(Conversation conversation)
    {
        _openingConversation = true;
        try
        {
            _conversation = conversation;
            var provider = Providers.FirstOrDefault(item => item.Id == conversation.ProviderId);
            if (provider is not null)
            {
                SelectedProvider = provider;
            }

            var skill = Skills.FirstOrDefault(item => item.Id == conversation.SkillId);
            if (skill is not null)
            {
                SelectedSkill = skill;
            }

            using var snapshot = JsonDocument.Parse(conversation.ProviderConfiguration);
            if (snapshot.RootElement.TryGetProperty("ModelId", out var modelId))
            {
                SelectedModelId = modelId.GetString();
            }

            Messages.Clear();
            foreach (var message in await _messageRepository.GetByConversationAsync(conversation.Id).ConfigureAwait(false))
            {
                Messages.Add(new ChatMessageViewModel(message.Role, message.Content));
            }

            StatusMessage = $"已打开：{conversation.Title}";
        }
        finally
        {
            _openingConversation = false;
        }
    }
}
