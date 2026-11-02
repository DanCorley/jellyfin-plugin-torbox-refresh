using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.TorBoxRefresh;

public class PluginConfiguration : BasePluginConfiguration
{
    public bool Enabled { get; set; } = true;

    public string RefreshUrl { get; set; } = "http://torbox-media-center:8787/refresh";

    public string Token { get; set; } = string.Empty;

    public int TimeoutSeconds { get; set; } = 120;

    public int CooldownSeconds { get; set; } = 300;

    public bool QueueFollowUpScan { get; set; } = true;
}
