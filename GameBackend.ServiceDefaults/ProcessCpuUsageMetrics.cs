using System.Diagnostics.Metrics;

namespace Microsoft.Extensions.Hosting;

/// <summary>
/// Publishes process.cpu.usage (0–1) as an observable gauge using Environment.CpuUsage (.NET 10).
/// </summary>
internal sealed class ProcessCpuUsageMetrics : BackgroundService
{
    internal const string MeterName = "GameBackend.ProcessMetrics";

    private readonly Meter _meter;

    public ProcessCpuUsageMetrics()
    {
        _meter = new Meter(MeterName);

        TimeSpan previousTotal = TimeSpan.Zero;
        DateTimeOffset previousTime = DateTimeOffset.UtcNow;

        _meter.CreateObservableGauge<double>(
            name: "process.cpu.usage",
            observeValue: () =>
            {
                var now = DateTimeOffset.UtcNow;
                var currentTotal = Environment.CpuUsage.TotalTime;

                var elapsedWall = (now - previousTime).TotalSeconds;
                var elapsedCpu = (currentTotal - previousTotal).TotalSeconds;

                previousTotal = currentTotal;
                previousTime = now;

                if (elapsedWall <= 0 || Environment.ProcessorCount <= 0)
                    return 0;

                return Math.Clamp(elapsedCpu / (elapsedWall * Environment.ProcessorCount), 0, 1);
            },
            unit: "1",
            description: "CPU usage ratio of the process (0–1). Accounts for all logical cores.");
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken) => Task.CompletedTask;

    public override void Dispose()
    {
        _meter.Dispose();
        base.Dispose();
    }
}
