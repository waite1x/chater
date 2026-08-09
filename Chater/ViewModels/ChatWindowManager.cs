using Chater.Views;
using Microsoft.Extensions.DependencyInjection;

namespace Chater.ViewModels;

public sealed class ChatWindowManager
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly AppState _appState;
    private readonly SemaphoreSlim _lock = new(1, 1);

    private IServiceScope? _groupScope;
    private readonly List<Window> _chatWindows = [];

    public ChatWindowManager(
        IServiceScopeFactory scopeFactory, AppState appState)
    {
        _scopeFactory = scopeFactory;
        _appState = appState;
    }

    public void Show()
    {
        var window = _chatWindows.LastOrDefault();
        if (window == null)
        {
            ShowNew();
        }
    }

    public void ShowNew()
    {
        ShowNew(null);
    }

    public void ShowNew(string? conversationId)
    {
        var window = CreateWindow();
        window.Show();
        window.Focus();

        _ = InitialWindowAsync(window, conversationId);
    }

    private ChatWindow CreateWindow()
    {
        _lock.Wait();

        try
        {
            // 第一个窗口：创建 WindowGroup Scope
            if (_groupScope == null)
            {
                var scope = _scopeFactory.CreateScope();
                try
                {
                    _groupScope = scope;
                    _ = _appState.RefreshConversationHistoryAsync().ConfigureAwait(false);
                }
                catch
                {
                    scope.Dispose();
                    throw;
                }
            }

            // 每个 Window 都创建一个独立 Scope
            var windowScope =
                _scopeFactory.CreateAsyncScope();

            try
            {
                var viewModel =
                    ActivatorUtilities.CreateInstance<ChatWindowViewModel>(
                        windowScope.ServiceProvider);

                var window =
                    ActivatorUtilities.CreateInstance<ChatWindow>(
                        windowScope.ServiceProvider,
                        viewModel);
                AttachWindowLifetime(
                    window,
                    windowScope);
                _chatWindows.Add(window);

                return window;
            }
            catch
            {
                windowScope.Dispose();
                throw;
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task InitialWindowAsync(Window window, string? conversationId = null)
    {
        var vm = window.DataContext as ChatWindowViewModel;
        if (vm is null)
        {
            return;
        }

        if (conversationId != null)
        {
            await vm.OpenConversationAsync(conversationId);
        }
    }

    private void AttachWindowLifetime(
        ChatWindow window,
        AsyncServiceScope windowScope)
    {
        EventHandler? handler = null;

        handler = (_, _) =>
        {
            window.Closed -= handler;

            _ = ReleaseWindowAsync(window, windowScope);
        };

        window.Closed += handler;
    }

    private async Task ReleaseWindowAsync(
        Window window,
        AsyncServiceScope windowScope)
    {
        // ---------------------------------
        // 首先释放这个 Window 自己的资源
        // ---------------------------------

        await windowScope.DisposeAsync();

        IServiceScope? groupScopeToDispose = null;

        await _lock.WaitAsync();

        try
        {
            _chatWindows.Remove(window);

            if (_chatWindows.Count == 0)
            {
                groupScopeToDispose = _groupScope;
                _groupScope = null;
                _appState.ClearConversationHistory();
            }
        }
        finally
        {
            _lock.Release();
        }

        // ---------------------------------
        // 最后一个 Window 才释放共享 Scope
        // ---------------------------------
        groupScopeToDispose?.Dispose();
    }
}