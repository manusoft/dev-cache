using Manuhub.Memora;

namespace ManuHub.Memora;

public class Worker(ILogger<Worker> logger, IConfiguration configuration) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var server = new MemoraServer(configuration, logger);

        try
        {
            await server.StartAsync(stoppingToken);
            logger.LogInformation("Memora server stopped gracefully");
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            logger.LogCritical(ex, "Memora failed");
            throw;
        }
        finally
        {
            server.Dispose();
            logger.LogInformation("[SHUTDOWN] All resources disposed");
        }
    }
}
