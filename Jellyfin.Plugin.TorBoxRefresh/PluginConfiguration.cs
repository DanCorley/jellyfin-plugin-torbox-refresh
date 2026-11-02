using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.TorBoxRefresh;

public class PluginConfiguration : BasePluginConfiguration
{
    public bool Enabled { get; set; } = true;

    public string RefreshUrl { get; set; } = "http://torbox-media-center:8787/refresh";

    public string Token { get; set; } = string.Empty;

    public int TimeoutSeconds { get; set; } = 120;

    public int CooldownSeconds { get; set; } = 300;

    /// <summary>
    /// Gets or sets a value indicating whether a follow-up library scan is queued
    /// when the TorBox refresh reports changes. The follow-up scan is needed because
    /// an <c>ILibraryPostScanTask</c> runs after Jellyfin has already walked the mount:
    /// the first scan refreshes the mount, and the second scan picks up the new files.
    /// The second scan's post-scan task then hits <see cref="CooldownSeconds"/> and stops.
    /// Defaults to <c>true</c>; a follow-up is only queued when <c>changed &gt; 0</c>.
    /// </summary>
    public bool QueueFollowUpScan { get; set; } = true;
}
