using System;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.TorBoxRefresh;

public sealed class TorBoxRefreshPostScanTask : ILibraryPostScanTask
{
    private readonly TorBoxRefreshService _service;
    private readonly ILibraryManager _libraryManager;
    private readonly ILogger<TorBoxRefreshPostScanTask> _logger;

    public TorBoxRefreshPostScanTask(
        TorBoxRefreshService service,
        ILibraryManager libraryManager,
        ILogger<TorBoxRefreshPostScanTask> logger)
    {
        _service = service;
        _libraryManager = libraryManager;
        _logger = logger;
    }

    public async Task Run(
        IProgress<double> progress,
        CancellationToken cancellationToken)
    {
        progress.Report(0);

        var result = await _service.RefreshAsync(cancellationToken).ConfigureAwait(false);

        if (result.Changed > 0
            && Plugin.Instance?.Configuration?.QueueFollowUpScan == true)
        {
            _logger.LogInformation(
                "TorBox mount changed ({Added} added, {Removed} removed, {Updated} updated); queueing follow-up scan",
                result.Added,
                result.Removed,
                result.Updated);

            _libraryManager.QueueLibraryScan();
        }

        progress.Report(100);
    }
}
