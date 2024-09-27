using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;    // BackgroundService
using Dotmim.Sync;                    // SyncAgent and synchronization components

public class SyncBackgroundService : BackgroundService
{
    private readonly SyncAgent _syncAgent;

    public SyncBackgroundService(SyncAgent syncAgent)
    {
        _syncAgent = syncAgent;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Thực hiện đồng bộ hóa
                var result = await _syncAgent.SynchronizeAsync();
                Console.WriteLine($"Sync done at {DateTime.Now}: {result.TotalChangesDownloaded} changes downloaded.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Sync failed: {ex.Message}");
            }

            // Chờ 5 phút
            await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
        }
    }
}
