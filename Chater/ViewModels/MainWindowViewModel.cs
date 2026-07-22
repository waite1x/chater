using System.Collections.ObjectModel;
using System.Text.Json;
using Avalonia.Input;
using Chater.Data;
using Chater.Models;
using Chater.Models.Enums;
using Chater.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Chater.ViewModels;

public sealed partial class MainWindowViewModel : ViewModelBase
{
    private readonly ProviderService _providerService;
    private readonly SkillRepository _skills;
    private readonly ConversationService _conversations;
    private readonly ChatService _chat;
    private readonly ConversationRepository _conversationRepository;
    private readonly MessageRepository _messageRepository;
    private readonly SkillService _skillService;
    private readonly IWindowNavigationService? _navigation;
    private readonly AppSettingsService _settings;
    private readonly IGlobalHotKeyService? _globalHotKeys;
    private Conversation? _conversation;
    private CancellationTokenSource? _sendCancellation;
    private bool _openingConversation;

    public MainWindowViewModel(ProviderService providerService, SkillRepository skills, ConversationService conversations, ChatService chat, ConversationRepository conversationRepository, MessageRepository messageRepository, SkillService skillService, AppSettingsService settings, IWindowNavigationService? navigation = null, IGlobalHotKeyService? globalHotKeys = null)
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
        _globalHotKeys = globalHotKeys;
    }

    public ObservableCollection<ApiProvider> Providers { get; } = [];
    public ObservableCollection<Skill> Skills { get; } = [];
    public ObservableCollection<ChatMessageViewModel> Messages { get; } = [];
    public ObservableCollection<Conversation> Conversations { get; } = [];
    public IReadOnlyList<ProviderType> ProviderTypes { get; } = Enum.GetValues<ProviderType>();

    public IReadOnlyList<string> AvailableModels => SelectedProvider?.ModelIds ?? [];
    public IReadOnlyList<ThemeOption> ThemeOptions { get; } = [new("system", "跟随系统"), new("light", "浅色"), new("dark", "深色")];

    [ObservableProperty]
    private ThemeOption? _selectedTheme;

    [ObservableProperty]
    private string _chatShortcut = AppSettingsService.DefaultChatShortcut;

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
    private string _draft = string.Empty;

    [ObservableProperty]
    private string _statusMessage = "正在加载配置…";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    private bool _isSending;

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
    private int _settingsTabIndex;

    public bool IsApiKeySettingsVisible => SettingsTabIndex == 0;
    public bool IsSkillSettingsVisible => SettingsTabIndex == 1;
    public bool IsGeneralSettingsVisible => SettingsTabIndex == 2;
    public bool IsShortcutSettingsVisible => SettingsTabIndex == 3;

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        var theme = await _settings.GetAsync(AppSettingsService.ThemeKey, cancellationToken).ConfigureAwait(false) ?? AppSettingsService.DefaultTheme;
        SelectedTheme = ThemeOptions.FirstOrDefault(item => item.Key == theme) ?? ThemeOptions[0];
        ChatShortcut = await _settings.GetAsync(AppSettingsService.ChatShortcutKey, cancellationToken).ConfigureAwait(false) ?? AppSettingsService.DefaultChatShortcut;
        AppSettingsService.ApplyTheme(SelectedTheme.Key);
        Providers.Clear();
        foreach (var provider in await _providerService.GetAllAsync(cancellationToken).ConfigureAwait(false))
        {
            if (provider.IsEnabled)
            {
                Providers.Add(provider);
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
        StatusMessage = SelectedProvider is null ? "请先在 SQLite 配置中添加并启用服务商。" : "已就绪。";
    }

    [RelayCommand]
    private void NewConversation()
    {
        _conversation = null;
        SelectedConversation = null;
        Messages.Clear();
        StatusMessage = "将于首次发送时创建新会话。";
    }

    [RelayCommand]
    private void OpenSettings() => _navigation?.ShowSettings();

    [RelayCommand]
    private void OpenSkillWorkbench() => _navigation?.ShowSkillSettings();

    [RelayCommand]
    private void ShowApiKeySettings() => SettingsTabIndex = 0;

    [RelayCommand]
    private void ShowSkillSettings() => SettingsTabIndex = 1;

    [RelayCommand]
    private void ShowGeneralSettings() => SettingsTabIndex = 2;

    [RelayCommand]
    private void ShowShortcutSettings() => SettingsTabIndex = 3;

    [RelayCommand]
    private async Task SaveGeneralSettingsAsync()
    {
        var theme = SelectedTheme?.Key ?? AppSettingsService.DefaultTheme;
        await _settings.SaveAsync(AppSettingsService.ThemeKey, theme).ConfigureAwait(false);
        AppSettingsService.ApplyTheme(theme);
        StatusMessage = "通用设置已保存。";
    }

    [RelayCommand]
    private async Task SaveShortcutSettingsAsync()
    {
        if (!ShortcutFormatter.TryParse(ChatShortcut, out _))
        {
            StatusMessage = "快捷键格式无效，请点击输入框后按下组合键。";
            return;
        }
        await _settings.SaveAsync(AppSettingsService.ChatShortcutKey, ChatShortcut).ConfigureAwait(false);
        _globalHotKeys?.UpdateShortcut(ChatShortcut);
        StatusMessage = "快捷键已保存。";
    }

    [RelayCommand]
    private void ShowChat() => _navigation?.ShowChat();

    public bool IsChatShortcut(Key key, KeyModifiers modifiers) => ShortcutFormatter.Matches(ChatShortcut, key, modifiers);

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
        StatusMessage = "填写服务商配置后保存。";
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
            StatusMessage = "服务商已保存。";
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
            StatusMessage = "正在测试连接…";
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
        StatusMessage = "填写自定义技能后保存。";
    }

    [RelayCommand]
    private async Task SaveSkillAsync()
    {
        var existing = SelectedSkill;
        if (existing?.IsBuiltIn == true)
        {
            StatusMessage = "内置技能不可编辑；请新增自定义技能。";
            return;
        }

        var now = DateTimeOffset.UtcNow;
        try
        {
            var saved = await _skillService.SaveCustomAsync(new Skill(existing?.Id ?? Guid.NewGuid().ToString("N"), SkillName, null, SkillPrompt, null, false, true, existing?.SortOrder ?? Skills.Count + 100, existing?.Version ?? 0, existing?.CreatedAt ?? now, now)).ConfigureAwait(false);
            await ReloadSkillsAsync().ConfigureAwait(false);
            SelectedSkill = Skills.FirstOrDefault(item => item.Id == saved.Id);
            StatusMessage = "技能已保存。";
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
            StatusMessage = "请先选择一个已启用的服务商。";
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
        StatusMessage = "正在生成回复…";
        try
        {
            await foreach (var update in _chat.SendStreamingAsync(_conversation.Id, text, _sendCancellation.Token).ConfigureAwait(false))
            {
                assistant.Content += update;
            }

            StatusMessage = "已完成。";
            await RefreshConversationHistoryAsync().ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "已停止生成。";
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

    private bool CanSend() => !IsSending && !string.IsNullOrWhiteSpace(Draft);
    private bool CanStop() => IsSending;

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

    partial void OnIsSendingChanged(bool value) => StopCommand.NotifyCanExecuteChanged();

    partial void OnSelectedProviderChanged(ApiProvider? value)
    {
        OnPropertyChanged(nameof(AvailableModels));
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
