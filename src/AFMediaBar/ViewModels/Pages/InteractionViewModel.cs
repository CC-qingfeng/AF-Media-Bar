using AFMediaBar.Classes.Settings;

namespace AFMediaBar.ViewModels.Pages;

/// <summary>任务栏静置内容与托盘图标的点击、滚轮结果绑定。 / Click and wheel-result bindings for taskbar rest content and the tray icon.</summary>
public partial class InteractionViewModel : ObservableObject
{
    public PlayerClickAction ArtworkClickAction
    {
        get => Current.ArtworkClickAction;
        set => Update(Current with { ArtworkClickAction = value });
    }

    public PlayerClickAction TextClickAction
    {
        get => Current.TextClickAction;
        set => Update(Current with { TextClickAction = value });
    }

    public WheelAction PrimaryWheelAction
    {
        get => Current.PrimaryWheelAction;
        set => Update(Current with { PrimaryWheelAction = value });
    }

    public InteractionModifier Modifier
    {
        get => Current.Modifier;
        set => Update(Current with { Modifier = value });
    }

    public WheelAction ChordWheelAction
    {
        get => Current.ChordWheelAction;
        set => Update(Current with { ChordWheelAction = value });
    }

    public TrayClickAction TrayClickAction
    {
        get => Current.TrayClickAction;
        set => Update(Current with { TrayClickAction = value });
    }

    public TrayWheelBehavior TrayPrimaryWheelAction
    {
        get => Current.TrayPrimaryWheelAction;
        set => Update(Current with { TrayPrimaryWheelAction = value });
    }

    public TrayWheelBehavior TrayChordWheelAction
    {
        get => Current.TrayChordWheelAction;
        set => Update(Current with { TrayChordWheelAction = value });
    }

    private static GlobalInteractionSettings Current => SettingsManager.Current.Interaction;

    public InteractionViewModel() => SettingsManager.SettingsChanged += OnSettingsChanged;

    private void Update(GlobalInteractionSettings settings)
    {
        SettingsManager.SetInteractionSettings(settings.Normalize());
        RaiseAll();
    }

    public void ResetInteraction() => SettingsManager.ResetInteraction();

    private void OnSettingsChanged(object? sender, SettingsChangedEventArgs e)
    {
        if (e.ResetScope is SettingsResetScope.Interaction or SettingsResetScope.All)
            RaiseAll();
    }

    private void RaiseAll()
    {
        OnPropertyChanged(nameof(ArtworkClickAction));
        OnPropertyChanged(nameof(TextClickAction));
        OnPropertyChanged(nameof(PrimaryWheelAction));
        OnPropertyChanged(nameof(Modifier));
        OnPropertyChanged(nameof(ChordWheelAction));
        OnPropertyChanged(nameof(TrayClickAction));
        OnPropertyChanged(nameof(TrayPrimaryWheelAction));
        OnPropertyChanged(nameof(TrayChordWheelAction));
    }
}
