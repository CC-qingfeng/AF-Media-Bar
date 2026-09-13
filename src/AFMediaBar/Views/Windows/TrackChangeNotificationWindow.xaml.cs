using System.Windows.Interop;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using AFMediaBar.Classes.Interop;
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Settings;
using AFMediaBar.Classes.Utils;
using Wpf.Ui.Controls;

namespace AFMediaBar.Views.Windows;

/// <summary>可复用、非激活的曲目切换通知窗口。 / Reusable, non-activating track-change notification window.</summary>
public partial class TrackChangeNotificationWindow : FluentWindow
{
    private readonly DispatcherTimer _hideTimer;
    private DisplayMonitorInfo? _currentMonitor;
    private TrackChangeNotificationPosition _currentPosition;
    private int _presentationVersion;

    /// <summary>创建通知窗口并接入统一窗口外观。 / Creates the notification window and attaches unified window appearance.</summary>
    public TrackChangeNotificationWindow(WindowAppearanceService appearanceService)
    {
        WindowHelper.SetNoActivate(this);
        InitializeComponent();
        Left = -10000;
        Top = -10000;
        appearanceService.AttachNonActivatingTransient(this);
        _hideTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _hideTimer.Tick += HideTimer_Tick;
    }

    /// <summary>更新并显示通知；重复调用会复用 HWND 并重置停留时间。 / Updates and shows the notification, reusing its HWND and resetting dwell time.</summary>
    public void ShowNotification(TrackChangeNotificationRequest request)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => ShowNotification(request));
            return;
        }

        _presentationVersion++;
        _hideTimer.Stop();
        StopAnimations();
        _currentMonitor = request.Monitor;
        _currentPosition = request.Settings.Position;
        ApplySnapshot(request.Snapshot);

        if (!IsVisible)
        {
            Opacity = 0;
            Show();
        }

        UpdateLayout();
        PositionOnMonitor(request.Monitor, request.Settings.Position);
        Opacity = 1;
        BeginEntryAnimation(request.Settings.Position);

        _hideTimer.Interval = TimeSpan.FromMilliseconds(request.Settings.DurationMilliseconds);
        _hideTimer.Start();
    }

    /// <summary>补全当前可见通知的封面或文字，不重新显示窗口或重置计时。 / Enriches artwork or text for the visible notification without re-showing it or resetting its timer.</summary>
    public void UpdateSnapshot(MediaSnapshot snapshot)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => UpdateSnapshot(snapshot));
            return;
        }

        if (!IsVisible || _currentMonitor is null)
            return;

        ApplySnapshot(snapshot);
        UpdateLayout();
        PositionOnMonitor(_currentMonitor, _currentPosition);
    }

    private void ApplySnapshot(MediaSnapshot snapshot)
    {
        TitleText.Text = snapshot.Title;
        ArtistText.Text = string.IsNullOrWhiteSpace(snapshot.Artist)
            ? snapshot.SourceName
            : snapshot.Artist;
        ArtworkImage.Source = snapshot.Artwork;
        ArtworkPlaceholder.Visibility = snapshot.Artwork is null ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>立即隐藏通知并取消动画和计时。 / Immediately hides the notification and cancels animations and timing.</summary>
    public void HideImmediately()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(HideImmediately);
            return;
        }

        _presentationVersion++;
        _hideTimer.Stop();
        StopAnimations();
        if (IsVisible)
            Hide();
    }

    private void PositionOnMonitor(DisplayMonitorInfo monitor, TrackChangeNotificationPosition position)
    {
        var scaleX = Math.Max(1d / 96d, monitor.DpiX / 96d);
        var scaleY = Math.Max(1d / 96d, monitor.DpiY / 96d);
        var pixelSize = NotificationPlacementCalculator.ToPhysicalSize(
            new Size(Math.Max(1, ActualWidth), Math.Max(1, ActualHeight)),
            monitor.DpiX,
            monitor.DpiY);
        var point = NotificationPlacementCalculator.Calculate(
            monitor.WorkArea,
            pixelSize,
            position,
            16 * Math.Max(scaleX, scaleY));
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero)
            return;

        NativeMethods.SetWindowPos(
            handle,
            -1,
            (int)Math.Round(point.X),
            (int)Math.Round(point.Y),
            (int)Math.Ceiling(pixelSize.Width),
            (int)Math.Ceiling(pixelSize.Height),
            NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW);
    }

    private void BeginEntryAnimation(TrackChangeNotificationPosition position)
    {
        var startsAtTop = position is TrackChangeNotificationPosition.TopLeft or
            TrackChangeNotificationPosition.TopCenter or TrackChangeNotificationPosition.TopRight;
        EntryTransform.Y = startsAtTop ? -12 : 12;
        AnimatedRoot.Opacity = 0;
        var duration = TimeSpan.FromMilliseconds(160);
        EntryTransform.BeginAnimation(
            TranslateTransform.YProperty,
            new DoubleAnimation(0, new Duration(duration))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            });
        AnimatedRoot.BeginAnimation(
            OpacityProperty,
            new DoubleAnimation(1, new Duration(duration)));
    }

    private void HideTimer_Tick(object? sender, EventArgs e)
    {
        _hideTimer.Stop();
        var version = _presentationVersion;
        var animation = new DoubleAnimation(0, new Duration(TimeSpan.FromMilliseconds(120)));
        animation.Completed += (_, _) =>
        {
            if (version == _presentationVersion && IsVisible)
                Hide();
        };
        AnimatedRoot.BeginAnimation(OpacityProperty, animation);
    }

    private void Window_MouseEnter(object sender, MouseEventArgs e) => HideImmediately();

    private void StopAnimations()
    {
        AnimatedRoot.BeginAnimation(OpacityProperty, null);
        EntryTransform.BeginAnimation(TranslateTransform.YProperty, null);
        AnimatedRoot.Opacity = 1;
        EntryTransform.Y = 0;
    }

    /// <summary>停止通知计时并释放窗口。 / Stops notification timing and releases the window.</summary>
    protected override void OnClosed(EventArgs e)
    {
        _hideTimer.Stop();
        _hideTimer.Tick -= HideTimer_Tick;
        StopAnimations();
        base.OnClosed(e);
    }
}
