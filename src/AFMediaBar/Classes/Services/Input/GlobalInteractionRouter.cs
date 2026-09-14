using AFMediaBar.Classes.Settings;
namespace AFMediaBar.Classes.Services;

/// <summary>解析并执行播放器表面共用的媒体滚轮语义。 / Resolves and executes media-wheel semantics shared by player surfaces.</summary>
public sealed class GlobalInteractionRouter
{
    private readonly MediaSessionService _mediaSessionService;

    public GlobalInteractionRouter(MediaSessionService mediaSessionService)
    {
        _mediaSessionService = mediaSessionService;
    }

    public async Task<string?> ExecuteWheelAsync(
        int delta,
        bool isLeftButtonDown,
        bool isRightButtonDown)
    {
        var settings = SettingsManager.Current.Interaction;
        var action = GlobalWheelGesturePolicy.Resolve(
            settings,
            isLeftButtonDown,
            isRightButtonDown);
        if (action is null || delta == 0)
            return null;

        var steps = WheelInput.GetStepCount(delta);
        switch (action.Value)
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
    public static WheelAction? Resolve(
        GlobalInteractionSettings settings,
        bool isLeftButtonDown,
        bool isRightButtonDown)
    {
        settings = settings.Normalize();
        if (settings.Mode == MediaInteractionMode.Buttons)
            return null;

        var chordPressed = settings.ChordWheelEnabled && settings.ChordButton switch
        {
            MouseChordButton.Left => isLeftButtonDown,
            MouseChordButton.Right => isRightButtonDown,
            _ => false
        };
        return chordPressed ? settings.ChordWheelAction : settings.PrimaryWheelAction;
    }
}
