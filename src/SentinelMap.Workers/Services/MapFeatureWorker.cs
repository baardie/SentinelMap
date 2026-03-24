using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NetTopologySuite.Geometries;
using SentinelMap.Domain.Entities;
using SentinelMap.Domain.Messages;
using SentinelMap.Infrastructure.Data;
using StackExchange.Redis;

namespace SentinelMap.Workers.Services;

/// <summary>
/// Subscribes to observations:AIS_INFRA and upserts MapFeature records.
/// Also subscribes to observations:AIS_SAFETY and creates safety broadcast alerts.
/// </summary>
public class MapFeatureWorker : BackgroundService
{
    private readonly IConnectionMultiplexer _redis;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<MapFeatureWorker> _logger;

    public MapFeatureWorker(
        IConnectionMultiplexer redis,
        IServiceScopeFactory scopeFactory,
        ILogger<MapFeatureWorker> logger)
    {
        _redis = redis;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var subscriber = _redis.GetSubscriber();

        // Subscribe to AIS infrastructure observations
        await subscriber.SubscribeAsync(
            RedisChannel.Literal("observations:AIS_INFRA"),
            async (_, message) =>
            {
                if (message.IsNull) return;

                try
                {
                    var msg = JsonSerializer.Deserialize<ObservationPublishedMessage>(message!);
                    if (msg is null) return;

                    await UpsertMapFeatureAsync(msg, stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing AIS_INFRA observation");
                }
            });

        // Subscribe to AIS safety broadcast observations
        await subscriber.SubscribeAsync(
            RedisChannel.Literal("observations:AIS_SAFETY"),
            async (_, message) =>
            {
                if (message.IsNull) return;

                try
                {
                    var msg = JsonSerializer.Deserialize<ObservationPublishedMessage>(message!);
                    if (msg is null) return;

                    await HandleSafetyBroadcastAsync(msg, stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing AIS_SAFETY observation");
                }
            });

        _logger.LogInformation("MapFeatureWorker subscribed to observations:AIS_INFRA and observations:AIS_SAFETY");
        await Task.Delay(Timeout.Infinite, stoppingToken);
    }

    private async Task UpsertMapFeatureAsync(ObservationPublishedMessage msg, CancellationToken ct)
    {
        // Parse RawData to get feature type and metadata
        string featureType = "AisBaseStation";
        string name = msg.ExternalId;
        int? aidType = null;

        if (msg.DisplayName is not null)
        {
            name = msg.DisplayName;
        }

        // The RawData isn't directly on ObservationPublishedMessage — we need to
        // extract featureType from DisplayName/VesselType fields, or query the observation.
        // Since RedisObservationPublisher extracts displayName from RawData, we can use
        // the VesselType field which isn't set for infra messages.
        // Better approach: query DB for the observation to get full RawData.

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SystemDbContext>();

        // Look up the observation to get RawData
        var observation = await db.Observations
            .Where(o => o.Id == msg.ObservationId)
            .Select(o => new { o.RawData })
            .FirstOrDefaultAsync(ct);

        if (observation?.RawData is not null)
        {
            try
            {
                var rawNode = JsonNode.Parse(observation.RawData);
                featureType = rawNode?["featureType"]?.GetValue<string>() ?? featureType;
                name = rawNode?["name"]?.GetValue<string>() ?? name;
                aidType = rawNode?["aidType"]?.GetValue<int>();
            }
            catch { /* use defaults */ }
        }

        // Check if this feature already exists by MMSI (ExternalId) and feature type
        var mmsiSearch = $"%\"mmsi\":\"{msg.ExternalId}\"%";
        var existing = await db.MapFeatures
            .FirstOrDefaultAsync(f => f.Source == "ais"
                && f.Details != null
                && EF.Functions.Like(f.Details, mmsiSearch),
                ct);

        if (existing is not null)
        {
            // Update position if it has changed
            existing.Position = new Point(msg.Longitude, msg.Latitude) { SRID = 4326 };
            existing.UpdatedAt = DateTimeOffset.UtcNow;
            existing.IsActive = true;
        }
        else
        {
            var icon = featureType switch
            {
                "AisBaseStation" => "base-station",
                "AidToNavigation" => "buoy",
                _ => "marker"
            };

            var color = featureType switch
            {
                "AisBaseStation" => "#6366f1",
                "AidToNavigation" => "#22c55e",
                _ => "#94a3b8"
            };

            var feature = new MapFeature
            {
                FeatureType = featureType,
                Name = name,
                Position = new Point(msg.Longitude, msg.Latitude) { SRID = 4326 },
                Icon = icon,
                Color = color,
                Details = JsonSerializer.Serialize(new { mmsi = msg.ExternalId, aidType }),
                Source = "ais",
                IsActive = true,
            };

            db.MapFeatures.Add(feature);
            _logger.LogInformation("Created MapFeature {FeatureType} for MMSI {Mmsi} at ({Lon}, {Lat})",
                featureType, msg.ExternalId, msg.Longitude, msg.Latitude);
        }

        await db.SaveChangesAsync(ct);
    }

    private async Task HandleSafetyBroadcastAsync(ObservationPublishedMessage msg, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SystemDbContext>();

        // Safety text is carried in DisplayName field of the published message
        string text = msg.DisplayName ?? "Safety broadcast received";
        string mmsi = msg.ExternalId;

        // Create a safety broadcast alert
        var alert = new Alert
        {
            Type = SharedKernel.Enums.AlertType.SafetyBroadcast,
            Severity = SharedKernel.Enums.AlertSeverity.Medium,
            Summary = $"Safety broadcast from MMSI {mmsi}: {(text.Length > 100 ? text[..100] + "..." : text)}",
            Details = JsonSerializer.Serialize(new { text, mmsi }),
            Status = SharedKernel.Enums.AlertStatus.Triggered,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        db.Alerts.Add(alert);
        await db.SaveChangesAsync(ct);

        // Publish alert notification
        var alertMsg = new AlertTriggeredMessage(
            AlertId: alert.Id,
            Type: alert.Type.ToString(),
            Severity: alert.Severity.ToString(),
            EntityId: null,
            Summary: alert.Summary,
            CreatedAt: alert.CreatedAt);

        var json = JsonSerializer.Serialize(alertMsg);
        await _redis.GetSubscriber().PublishAsync(RedisChannel.Literal("alerts:triggered"), json);

        _logger.LogInformation("Safety broadcast alert created from MMSI {Mmsi}", mmsi);
    }
}
