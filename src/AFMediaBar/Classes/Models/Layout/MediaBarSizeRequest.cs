namespace AFMediaBar.Classes.Models.Layout;

/// <summary>
/// 媒体栏内容尺寸请求：由媒体控件提出，由任务栏或灵动岛宿主执行过渡。
/// Media-bar content size request: raised by the media control and applied by its host.
/// </summary>
/// <param name="IsForcedRefresh">
/// 该请求是否绕过了内容指纹去重。宿主明确刷新布局状态时置为 true，避免宿主尚未就绪期间被丢弃的请求把后续权威刷新去重掉。
/// Whether the request bypassed the content-fingerprint dedupe. The host sets it when it explicitly refreshes its layout
/// state so an earlier request dropped while the host was not ready cannot dedupe away this authoritative refresh.
/// </param>
public sealed record MediaBarSizeRequest(
    LayoutOrientation Orientation,
    double Width,
    double Height,
    string ContentFingerprint,
    bool IsForcedRefresh)
{
    /// <summary>获取当前布局主轴目标尺寸。/ Gets the target size along the layout primary axis.</summary>
    public double PrimaryLength => Orientation == LayoutOrientation.Horizontal ? Width : Height;
}

/// <summary>媒体栏尺寸请求事件参数。/ Event arguments for a media-bar size request.</summary>
public sealed class MediaBarSizeRequestEventArgs(MediaBarSizeRequest request) : EventArgs
{
    public MediaBarSizeRequest Request { get; } = request;
}
