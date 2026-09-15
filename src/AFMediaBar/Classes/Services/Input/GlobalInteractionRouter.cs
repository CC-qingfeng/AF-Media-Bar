using AFMediaBar.Classes.Settings;
using AFMediaBar.Classes.Services.Audio;
namespace AFMediaBar.Classes.Services;

/// <summary>解析并执行播放器表面共用的媒体滚轮语义。 / Resolves and executes media-wheel semantics shared by player surfaces.</summary>
public sealed class GlobalInteractionRouter
{
    private readonly MediaSessionService _mediaSessionService;
    private readonly AudioInteractionService _audioInteractionService;

    public GlobalInteractionRouter(
        MediaSessionService mediaSessionService,
        AudioInteractionService audioInteractionService)
    {
        _mediaSessionService = mediaSessionService;
        _audioInteractionService = audioInteractionService;
    }

    public async Task<string?> ExecuteWheelAsync(
        int delta,
        bool isShiftDown,
        bool isLeftButtonDown,
        bool isRightButtonDown)
    {
        var settings = SettingsManager.Current.Interaction;
        var action = GlobalWheelGesturePolicy.Resolve(
            settings,
            isShiftDown,
            isLeftButtonDown,
            isRightButtonDown);
        if (delta == 0)
            return null;

        var steps = WheelInput.GetStepCount(delta);
        switch (action)
        {
            case WheelAction.PreviousNext:
                for (var index = 0; index < steps; index++)
                {
                    if (delta > 0)
                        await _mediaSessionService.SkipPreviousAsync();
                    else
                        await _mediaSessionService.SkipNextAsync();
                }
                return delta > 0 ? "上一首" : "下一首";
            case WheelAction.SwitchMediaSource:
                return CycleMediaSource(delta > 0 ? -steps : steps);
            case WheelAction.CurrentApplicationVolume:
                return await _audioInteractionService.AdjustCurrentMediaVolumeAsync(delta > 0 ? steps : -steps);
            case WheelAction.OutputDevice:
                return await _audioInteractionService.CycleOutputDeviceAsync(delta > 0 ? -steps : steps, deferApply: true);
            default:
                return null;
        }
    }

    private string? CycleMediaSource(int signedSteps)
    {
        var sessions = _mediaSessionService.CurrentSessionOptions;
        if (sessions.Count == 0 || signedSteps == 0)
            return null;

        var current = sessions.ToList().FindIndex(session => session.IsSelected);
        if (current < 0)
            current = 0;
        var target = sessions[WheelInput.MoveCircular(current, signedSteps, sessions.Count)];
        _mediaSessionService.SelectSession(target.Key);
        return $"媒体源：{target.DisplayName}";
    }
}

/// <summary>无 UI 依赖的全局滚轮映射。 / UI-independent global wheel mapping.</summary>
public static class GlobalWheelGesturePolicy
{
    public static WheelAction Resolve(
        GlobalInteractionSettings settings,
        bool isShiftDown,
        bool isLeftButtonDown,
        bool isRightButtonDown)
    {
        settings = settings.Normalize();
        var chordPressed = settings.Modifier switch
        {
            InteractionModifier.Shift => isShiftDown,
            InteractionModifier.LeftMouseButton => isLeftButtonDown,
            InteractionModifier.RightMouseButton => isRightButtonDown,
            _ => false
        };
        return chordPressed ? settings.ChordWheelAction : settings.PrimaryWheelAction;
    }

    /// <summary>解析同一修饰键下的托盘滚轮绑定。 / Resolves tray-wheel binding using the shared modifier.</summary>
    public static TrayWheelBehavior ResolveTray(
        GlobalInteractionSettings settings,
        bool isShiftDown,
        bool isLeftButtonDown,
        bool isRightButtonDown)
    {
        settings = settings.Normalize();
        var chordPressed = settings.Modifier switch
        {
            InteractionModifier.Shift => isShiftDown,
            InteractionModifier.LeftMouseButton => isLeftButtonDown,
            InteractionModifier.RightMouseButton => isRightButtonDown,
            _ => false
        };
        return chordPressed ? settings.TrayChordWheelAction : settings.TrayPrimaryWheelAction;
    }
}

/// <summary>解析任务栏静置内容的点击绑定。 / Resolves click bindings for taskbar rest-layer content.</summary>
public static class PlayerClickBindingPolicy
{
    public static PlayerClickAction Resolve(GlobalInteractionSettings settings, bool artwork) =>
        artwork ? settings.Normalize().ArtworkClickAction : settings.Normalize().TextClickAction;
}
