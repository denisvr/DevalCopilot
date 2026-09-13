using Microsoft.Extensions.Hosting;

namespace DevalCopilot.Api.Security.Bootstrap;

/// <summary>
/// After the single bootstrap frame is consumed, the shell writes nothing further and
/// holds its end of the stdin pipe open for as long as it runs — that open pipe is the
/// shell-liveness signal, not further protocol. This keeps reading the same stream:
/// reaching EOF means the shell disappeared and requests orderly shutdown; any further
/// non-empty bytes (a duplicated frame, or anything else) is itself a protocol violation
/// and also fails closed by shutting down, rather than being parsed as a new bootstrap.
/// </summary>
public static class BootstrapStdinMonitor
{
    public static async Task RunAsync(
        Stream input, byte[] alreadyBuffered, IHostApplicationLifetime lifetime, ILogger logger, CancellationToken cancellationToken)
    {
        if (alreadyBuffered.Length > 0)
        {
            logger.LogError("Unexpected data followed the bootstrap frame; shutting down.");
            lifetime.StopApplication();
            return;
        }

        var buffer = new byte[256];
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var read = await input.ReadAsync(buffer, cancellationToken);
                if (read == 0)
                {
                    logger.LogInformation("The shell's stdin pipe closed; shutting down.");
                    lifetime.StopApplication();
                    return;
                }

                logger.LogError("Unexpected data arrived on stdin after the bootstrap frame; shutting down.");
                lifetime.StopApplication();
                return;
            }
        }
        catch (OperationCanceledException)
        {
            // Shutdown already in progress for another reason.
        }
    }
}
