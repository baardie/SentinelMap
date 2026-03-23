using SentinelMap.Domain.Entities;
using SentinelMap.Domain.Interfaces;
using SentinelMap.SharedKernel.Enums;
using StackExchange.Redis;

namespace SentinelMap.Infrastructure.Alerting;

public class GeofenceBreachRule : IAlertRule
{
    private readonly IGeofenceRepository _geofenceRepo;
    private readonly IDatabase _redisDb;

    public GeofenceBreachRule(IGeofenceRepository geofenceRepo, IDatabase redisDb)
    {
        _geofenceRepo = geofenceRepo;
        _redisDb = redisDb;
    }

    public string RuleId => "geofence-breach";
    public AlertType Type => AlertType.GeofenceBreach;

    public async Task<IReadOnlyList<AlertTrigger>> EvaluateAsync(TrackedEntity entity, CancellationToken ct = default)
    {
        if (entity.LastKnownPosition is null)
            return [];

        var membershipKey = $"geofence:membership:{entity.Id}";

        // Get current geofence memberships
        var currentIds = await _geofenceRepo.FindContainingAsync(entity.LastKnownPosition, ct);
        var currentSet = new HashSet<Guid>(currentIds);

        // Get previous memberships from Redis
        var previousMembers = await _redisDb.SetMembersAsync(membershipKey);
        var previousSet = new HashSet<Guid>(
            previousMembers.Select(m => Guid.Parse(m.ToString())));

        var triggers = new List<AlertTrigger>();

        // Entered geofences (in current but not previous)
        foreach (var geofenceId in currentSet.Except(previousSet))
        {
            triggers.Add(new AlertTrigger(
                Type: AlertType.GeofenceBreach,
                Severity: AlertSeverity.High,
                Summary: $"Entity {entity.DisplayName} entered geofence",
                Details: $"Entity {entity.Id} entered geofence {geofenceId}",
                GeofenceId: geofenceId));
        }

        // Exited geofences (in previous but not current)
        foreach (var geofenceId in previousSet.Except(currentSet))
        {
            triggers.Add(new AlertTrigger(
                Type: AlertType.GeofenceBreach,
                Severity: AlertSeverity.High,
                Summary: $"Entity {entity.DisplayName} exited geofence",
                Details: $"Entity {entity.Id} exited geofence {geofenceId}",
                GeofenceId: geofenceId));
        }

        // Update Redis membership SET
        await _redisDb.KeyDeleteAsync(membershipKey);
        if (currentSet.Count > 0)
        {
            var redisValues = currentSet.Select(id => (RedisValue)id.ToString()).ToArray();
            await _redisDb.SetAddAsync(membershipKey, redisValues);
        }

        return triggers;
    }
}
