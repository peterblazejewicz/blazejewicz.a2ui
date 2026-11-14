using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.Linq;
using System.Threading;
using Blazejewicz.A2Ui.Core.Components;
using Blazejewicz.A2Ui.Core.Messages;
using Blazejewicz.A2Ui.Core.Surfaces;

namespace Blazejewicz.A2Ui.Core.Tests;

public sealed class A2UiMetricsTests
{
    private sealed record RecordedMeasurement(
        string InstrumentName,
        string? Unit,
        long Value,
        IReadOnlyDictionary<string, object?> Tags
    );

    private sealed class ScopedMeterCapture : IDisposable
    {
        private readonly AsyncLocal<bool> _isInScope = new();
        private readonly MeterListener _listener = new();
        private readonly ConcurrentQueue<RecordedMeasurement> _scoped = new();
        private readonly ConcurrentQueue<string> _forbiddenTagViolations = new();

        public ScopedMeterCapture()
        {
            this._isInScope.Value = true;

            this._listener.InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == A2UiTelemetry.MeterName)
                {
                    listener.EnableMeasurementEvents(instrument);
                }
            };

            this._listener.SetMeasurementEventCallback<long>((instrument, measurement, tags, _) =>
                this.Record(instrument, measurement, tags)
            );
            this._listener.SetMeasurementEventCallback<int>((instrument, measurement, tags, _) =>
                this.Record(instrument, measurement, tags)
            );

            this._listener.Start();
        }

        public IReadOnlyList<RecordedMeasurement> Measurements => [.. this._scoped];

        public IReadOnlyList<string> ForbiddenTagViolations => [.. this._forbiddenTagViolations];

        private void Record(Instrument instrument, long value, ReadOnlySpan<KeyValuePair<string, object?>> tags)
        {
            var tagMap = new Dictionary<string, object?>(tags.Length, StringComparer.Ordinal);
            foreach (KeyValuePair<string, object?> kv in tags)
            {
                if (kv.Key == "a2ui.surface_id")
                {
                    this._forbiddenTagViolations.Enqueue(instrument.Name);
                }

                tagMap[kv.Key] = kv.Value;
            }

            if (this._isInScope.Value)
            {
                this._scoped.Enqueue(new RecordedMeasurement(instrument.Name, instrument.Unit, value, tagMap));
            }
        }

        public void Dispose()
        {
            this._isInScope.Value = false;
            this._listener.Dispose();
        }
    }

    [Fact]
    public void A2UiTelemetry_ExposesCanonicalSourceAndMeterNames()
    {
        Assert.Equal("A2Ui.Core", A2UiTelemetry.ActivitySourceName);
        Assert.Equal("A2Ui.Core", A2UiTelemetry.MeterName);
        Assert.Equal("a2ui.surface.operations", A2UiTelemetry.SurfaceOperationsInstrumentName);
        Assert.Equal("a2ui.surfaces.active", A2UiTelemetry.ActiveSurfacesInstrumentName);
        Assert.Equal("a2ui.errors", A2UiTelemetry.ErrorsInstrumentName);
    }

    [Fact]
    public void SurfaceManager_LifecycleAndErrors_EmitsBoundedMetricsAndNeverIncludesSurfaceId()
    {
        using var capture = new ScopedMeterCapture();

        var sm = new SurfaceManager(
            options: new SurfaceManagerOptions
            {
                StrictMode = false,
                RegisteredCatalogIds = new HashSet<string>(StringComparer.Ordinal) { A2UiProtocol.BasicCatalogIdV10 },
            }
        );

        // 1. Create s1 (+1 active) and s2 (+1 active)
        sm.Process(
            $$"""{"version":"1.0","createSurface":{"surfaceId":"s1","catalogId":"{{A2UiProtocol.BasicCatalogIdV10}}" } }"""
        );
        sm.Process(
            $$"""{"version":"1.0","createSurface":{"surfaceId":"s2","catalogId":"{{A2UiProtocol.BasicCatalogIdV10}}" } }"""
        );

        // 2. Duplicate createSurface on s1 -> INTEGRITY_ERROR, must NOT increment active surfaces
        Assert.Throws<A2UiMessageValidationException>(() =>
            sm.Process(
                $$"""{"version":"1.0","createSurface":{"surfaceId":"s1","catalogId":"{{A2UiProtocol.BasicCatalogIdV10}}" } }"""
            )
        );

        // 3. Unknown catalog -> CATALOG_NOT_FOUND, must NOT increment active surfaces
        Assert.Throws<A2UiMessageValidationException>(() =>
            sm.Process("""{"version":"1.0","createSurface":{"surfaceId":"s_bad_cat","catalogId":"unregistered"}}""")
        );

        // 4. UpdateComponents + UpdateDataModel ok + UpdateDataModel DATA_ERROR (traverse through scalar)
        sm.Process(
            new AgentToRendererMessage
            {
                Version = A2UiProtocol.Version10,
                UpdateComponents = new UpdateComponents
                {
                    SurfaceId = "s1",
                    Components = [new TextComponent { Id = "root" }],
                },
            }
        );
        sm.Process("""{"version":"1.0","updateDataModel":{"surfaceId":"s1","path":"/user","value":"Ada"}}""");
        sm.Process("""{"version":"1.0","updateDataModel":{"surfaceId":"s1","path":"/user/city","value":"London"}}""");

        // 5. Delete existing s1 (-1 active), then silent no-op delete of unknown surface (must NOT decrement!)
        sm.Process("""{"version":"1.0","deleteSurface":{"surfaceId":"s1"}}""");
        sm.Process("""{"version":"1.0","deleteSurface":{"surfaceId":"does_not_exist"}}""");

        var activeMeasurements = capture
            .Measurements.Where(m => m.InstrumentName == A2UiTelemetry.ActiveSurfacesInstrumentName)
            .ToList();
        Assert.Equal([1L, 1L, -1L], activeMeasurements.Select(m => m.Value).ToArray());
        Assert.All(activeMeasurements, m => Assert.Equal("{surface}", m.Unit));
        Assert.Equal(1L, activeMeasurements.Sum(m => m.Value));

        // 6. Clear remaining s2 -> decrements by -1 back to 0 net active surfaces
        sm.Clear();
        sm.Clear(); // Empty Clear() emits no 0-delta measurement

        activeMeasurements = capture
            .Measurements.Where(m => m.InstrumentName == A2UiTelemetry.ActiveSurfacesInstrumentName)
            .ToList();
        Assert.Equal([1L, 1L, -1L, -1L], activeMeasurements.Select(m => m.Value).ToArray());
        Assert.Equal(0L, activeMeasurements.Sum(m => m.Value));

        // Verify a2ui.surface.operations counts and tags
        var opMeasurements = capture
            .Measurements.Where(m => m.InstrumentName == A2UiTelemetry.SurfaceOperationsInstrumentName)
            .ToList();
        Assert.Equal(9, opMeasurements.Count);
        Assert.All(opMeasurements, m => Assert.Equal("{operation}", m.Unit));

        Assert.Equal(
            2,
            opMeasurements.Count(m =>
                Equals(m.Tags["a2ui.operation.type"], "createSurface") && Equals(m.Tags["a2ui.status"], "ok")
            )
        );
        Assert.Equal(
            2,
            opMeasurements.Count(m =>
                Equals(m.Tags["a2ui.operation.type"], "createSurface") && Equals(m.Tags["a2ui.status"], "error")
            )
        );
        Assert.Equal(
            1,
            opMeasurements.Count(m =>
                Equals(m.Tags["a2ui.operation.type"], "updateDataModel") && Equals(m.Tags["a2ui.status"], "error")
            )
        );

        // Verify a2ui.errors counts and error codes
        var errorMeasurements = capture
            .Measurements.Where(m => m.InstrumentName == A2UiTelemetry.ErrorsInstrumentName)
            .ToList();
        Assert.Equal(3, errorMeasurements.Count);
        Assert.All(errorMeasurements, m => Assert.Equal("{error}", m.Unit));
        Assert.Single(errorMeasurements, m => Equals(m.Tags["a2ui.error.code"], "INTEGRITY_ERROR"));
        Assert.Single(errorMeasurements, m => Equals(m.Tags["a2ui.error.code"], "CATALOG_NOT_FOUND"));
        Assert.Single(errorMeasurements, m => Equals(m.Tags["a2ui.error.code"], "DATA_ERROR"));

        // Cardinality rule: a2ui.surface_id must NEVER appear on any metric tag
        Assert.Empty(capture.ForbiddenTagViolations);
    }
}
