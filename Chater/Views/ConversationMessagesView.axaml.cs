using System.Collections;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace Chater.Views;

public partial class ConversationMessagesView : UserControl
{
    private const double BottomTolerance = 1;
    private bool _followTail = true;
    private bool _scrollPending;

    public static readonly StyledProperty<IEnumerable?> ItemsSourceProperty =
        AvaloniaProperty.Register<ConversationMessagesView, IEnumerable?>(nameof(ItemsSource));

    public IEnumerable? ItemsSource { get => GetValue(ItemsSourceProperty); set => SetValue(ItemsSourceProperty, value); }

    public ConversationMessagesView() => InitializeComponent();

    private void OnLoaded(object? sender, RoutedEventArgs e) => RequestScrollToEnd();

    private void OnMessagesScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        // Content growth does not move the offset, so it must not disable tail
        // following. Only a position change represents scrolling by the user
        // or the requested scroll-to-end operation.
        if (e.OffsetDelta.Y != 0)
        {
            _followTail = IsAtBottom();
        }

        if (_followTail && e.ExtentDelta.Y > 0)
        {
            RequestScrollToEnd();
        }
    }

    private bool IsAtBottom() =>
        MessagesScrollViewer.Offset.Y + MessagesScrollViewer.Viewport.Height >=
        MessagesScrollViewer.Extent.Height - BottomTolerance;

    private void RequestScrollToEnd()
    {
        if (!_followTail || _scrollPending)
        {
            return;
        }

        _scrollPending = true;
        Dispatcher.UIThread.Post(() =>
        {
            _scrollPending = false;
            if (_followTail)
            {
                MessagesScrollViewer.ScrollToEnd();
            }
        }, DispatcherPriority.Render);
    }
}
