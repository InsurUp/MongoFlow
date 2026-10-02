using System.Diagnostics.Metrics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;

namespace MongoFlow.Tests;

/// <summary>
/// <see cref="VaultMetrics"/>, registered by <c>AddMongoVault</c>, measures on a meter from the <see cref="IMeterFactory"/>
/// in DI, which keeps each provider's measurements apart, and without one on a meter of its own, disposed with the
/// provider.
/// </summary>
public class VaultMetricsTests
{
    [Test]
    public async Task AddMongoVault_WithAMeterFactory_MeasuresOnTheFactorysMeter()
    {
        // Arrange
        await using var host = new VaultHost<ShopVault>(services: services => services.AddMetrics());
        var factory = host.Services.GetRequiredService<IMeterFactory>();
        using var saves = new MetricCollector<double>(factory, MongoFlowTelemetry.MeterName, MongoFlowTelemetry.Instruments.SaveDuration);

        // Act
        host.Services.GetRequiredService<VaultMetrics>().RecordSave("ShopVault", joined: false, TimeSpan.FromMilliseconds(250), exception: null);

        // Assert
        await Verify(saves.GetMeasurementSnapshot().Select(measurement => new { measurement.Value, measurement.Tags }));
    }

    [Test]
    public async Task AddMongoVault_WithoutAMeterFactory_MeasuresOnAMeterDisposedWithTheProvider()
    {
        // Arrange
        var host = new VaultHost<ShopVault>();
        var metrics = host.Services.GetRequiredService<VaultMetrics>();
        using var saves = new MetricCollector<double>(metrics.SaveDuration);
        var completed = false;
        using var listener = new MeterListener();
        listener.MeasurementsCompleted = (instrument, _) => completed |= instrument == metrics.SaveDuration;
        listener.EnableMeasurementEvents(metrics.SaveDuration);
        metrics.RecordSave("ShopVault", joined: true, TimeSpan.FromMilliseconds(250), new TimeoutException());

        // Act
        await host.DisposeAsync();

        // Assert
        await Verify(new
        {
            Measurements = saves.GetMeasurementSnapshot().Select(measurement => new { measurement.Value, measurement.Tags }),
            Completed = completed
        });
    }
}
