using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using SentinelMap.Domain.Entities;
using SentinelMap.Infrastructure.Data;

namespace SentinelMap.Infrastructure.Auth;

public class RefreshTokenService
{
    private readonly SystemDbContext _db;

    public RefreshTokenService(SystemDbContext db) { _db = db; }

    public async Task<(string rawToken, RefreshToken entity)> CreateAsync(
        Guid userId, string? deviceInfo, CancellationToken ct = default)
    {
        // Limit to 5 active families per user — revoke oldest if exceeded
        var activeFamilies = await _db.RefreshTokens
            .Where(t => t.UserId == userId && !t.IsRevoked && t.ExpiresAt > DateTimeOffset.UtcNow)
            .GroupBy(t => t.FamilyId)
            .Select(g => new { FamilyId = g.Key, Created = g.Min(t => t.CreatedAt) })
            .OrderByDescending(f => f.Created)
            .ToListAsync(ct);

        if (activeFamilies.Count >= 5)
        {
            var oldestFamilies = activeFamilies.Skip(4).Select(f => f.FamilyId).ToList();
            await _db.RefreshTokens
                .Where(t => oldestFamilies.Contains(t.FamilyId))
                .ExecuteUpdateAsync(t => t.SetProperty(r => r.IsRevoked, true), ct);
        }

        var rawToken = GenerateRawToken();
        var entity = new RefreshToken
        {
            UserId = userId,
            TokenHash = HashToken(rawToken),
            FamilyId = Guid.NewGuid().ToString(),
            DeviceInfo = deviceInfo,
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(7),
        };

        _db.RefreshTokens.Add(entity);
        await _db.SaveChangesAsync(ct);

        return (rawToken, entity);
    }

    public async Task<(RefreshToken? oldToken, string? newRawToken, RefreshToken? newEntity)> ValidateAndRotateAsync(
        string rawToken, string? deviceInfo, CancellationToken ct = default)
    {
        var hash = HashToken(rawToken);
        var token = await _db.RefreshTokens
            .FirstOrDefaultAsync(t => t.TokenHash == hash, ct);

        if (token is null) return (null, null, null);

        // Reuse detection: if token is already revoked, revoke entire family
        if (token.IsRevoked)
        {
            await _db.RefreshTokens
                .Where(t => t.FamilyId == token.FamilyId)
                .ExecuteUpdateAsync(t => t.SetProperty(r => r.IsRevoked, true), ct);
            return (null, null, null);
        }

        if (token.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            token.IsRevoked = true;
            await _db.SaveChangesAsync(ct);
            return (null, null, null);
        }

        // Revoke old token
        token.IsRevoked = true;
        token.LastUsedAt = DateTimeOffset.UtcNow;

        // Issue new token in same family
        var newRawToken = GenerateRawToken();
        var newEntity = new RefreshToken
        {
            UserId = token.UserId,
            TokenHash = HashToken(newRawToken),
            FamilyId = token.FamilyId, // Same family
            DeviceInfo = deviceInfo ?? token.DeviceInfo,
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(7),
        };

        _db.RefreshTokens.Add(newEntity);
        await _db.SaveChangesAsync(ct);

        return (token, newRawToken, newEntity);
    }

    public async Task RevokeAllForUserAsync(Guid userId, CancellationToken ct = default)
    {
        await _db.RefreshTokens
            .Where(t => t.UserId == userId && !t.IsRevoked)
            .ExecuteUpdateAsync(t => t.SetProperty(r => r.IsRevoked, true), ct);
    }

    private static string GenerateRawToken()
    {
        var bytes = new byte[32];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static string HashToken(string rawToken)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(rawToken);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
