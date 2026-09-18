using AFMediaBar.Classes.Settings;

namespace AFMediaBar.Classes.Services;

/// <summary>针对稳定应用来源标识执行 SMTC 允许列表判断。 / Applies the SMTC allow-list to stable application source identifiers.</summary>
public static class MediaSourceFilterPolicy
{
    public static bool IsAllowed(string? sourceId, SmtcSourceFilterSettings settings)
    {
        settings = settings.Normalize();
        if (!settings.Enabled)
            return true;
        if (string.IsNullOrWhiteSpace(sourceId))
            return false;
        return settings.AllowedSourceIds!.Contains(sourceId.Trim(), StringComparer.OrdinalIgnoreCase);
    }
}
