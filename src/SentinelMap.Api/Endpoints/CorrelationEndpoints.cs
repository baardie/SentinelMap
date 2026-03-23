using Microsoft.EntityFrameworkCore;
using SentinelMap.Domain.Entities;
using SentinelMap.Infrastructure.Data;
using SentinelMap.SharedKernel.Enums;
using SentinelMap.SharedKernel.Interfaces;
using StackExchange.Redis;

namespace SentinelMap.Api.Endpoints;

public static class CorrelationEndpoints
{
    public static void MapCorrelationEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/correlations").WithTags("Correlations");

        group.MapGet("/pending", GetPending).RequireAuthorization("AnalystAccess");
        group.MapGet("/{id:guid}", GetById).RequireAuthorization("AnalystAccess");
        group.MapPost("/{id:guid}/approve", Approve).RequireAuthorization("AnalystAccess");
        group.MapPost("/{id:guid}/reject", Reject).RequireAuthorization("AnalystAccess");
    }

    private static async Task<IResult> GetPending(
        SentinelMapDbContext db,
        CancellationToken ct)
    {
        var reviews = await db.CorrelationReviews
            .Include(r => r.SourceEntity)
            .Include(r => r.TargetEntity)
            .Where(r => r.Status == "Pending")
            .OrderByDescending(r => r.CreatedAt)
            .Select(r => new CorrelationReviewResponse(
                r.Id,
                r.SourceEntityId, r.SourceEntity.DisplayName, r.SourceEntity.Type.ToString(),
                r.TargetEntityId, r.TargetEntity.DisplayName, r.TargetEntity.Type.ToString(),
                r.Confidence, r.RuleScores, r.Status,
                r.CreatedAt))
            .ToListAsync(ct);

        return Results.Ok(reviews);
    }

    private static async Task<IResult> GetById(
        Guid id,
        SentinelMapDbContext db,
        CancellationToken ct)
    {
        var review = await db.CorrelationReviews
            .Include(r => r.SourceEntity)
            .Include(r => r.TargetEntity)
            .Where(r => r.Id == id)
            .Select(r => new CorrelationReviewResponse(
                r.Id,
                r.SourceEntityId, r.SourceEntity.DisplayName, r.SourceEntity.Type.ToString(),
                r.TargetEntityId, r.TargetEntity.DisplayName, r.TargetEntity.Type.ToString(),
                r.Confidence, r.RuleScores, r.Status,
                r.CreatedAt))
            .FirstOrDefaultAsync(ct);

        return review is null ? Results.NotFound() : Results.Ok(review);
    }

    private static async Task<IResult> Approve(
        Guid id,
        SentinelMapDbContext db,
        IUserContext userContext,
        IConnectionMultiplexer redis,
        CancellationToken ct)
    {
        var review = await db.CorrelationReviews
            .Include(r => r.SourceEntity).ThenInclude(e => e.Identifiers)
            .Include(r => r.TargetEntity).ThenInclude(e => e.Identifiers)
            .FirstOrDefaultAsync(r => r.Id == id, ct);

        if (review is null) return Results.NotFound();
        if (review.Status != "Pending")
            return Results.Conflict(new { message = $"Review is already {review.Status}." });

        // Merge: move source entity's identifiers to target entity
        foreach (var identifier in review.SourceEntity.Identifiers.ToList())
        {
            var alreadyExists = review.TargetEntity.Identifiers
                .Any(i => string.Equals(i.IdentifierValue, identifier.IdentifierValue, StringComparison.OrdinalIgnoreCase)
                       && string.Equals(i.IdentifierType, identifier.IdentifierType, StringComparison.OrdinalIgnoreCase));

            if (!alreadyExists)
            {
                review.TargetEntity.Identifiers.Add(new EntityIdentifier
                {
                    EntityId = review.TargetEntityId,
                    IdentifierType = identifier.IdentifierType,
                    IdentifierValue = identifier.IdentifierValue,
                    Source = identifier.Source,
                    FirstSeen = identifier.FirstSeen,
                    LastSeen = identifier.LastSeen,
                });
            }
        }

        // Soft-delete source entity
        review.SourceEntity.Status = EntityStatus.Lost;
        review.SourceEntity.UpdatedAt = DateTimeOffset.UtcNow;

        // Mark review as approved
        review.Status = "Approved";
        review.ReviewedBy = userContext.UserId;
        review.ReviewedAt = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(ct);

        // Update Redis correlation cache — point source's identifiers to target entity
        var redisDb = redis.GetDatabase();
        foreach (var identifier in review.SourceEntity.Identifiers)
        {
            var sourceType = identifier.Source;
            var cacheKey = $"correlation:link:{sourceType}:{identifier.IdentifierValue}";
            await redisDb.StringSetAsync(cacheKey, review.TargetEntityId.ToString(), TimeSpan.FromDays(7));
        }

        return Results.Ok(new
        {
            review.Id,
            review.Status,
            review.ReviewedBy,
            review.ReviewedAt,
            MergedInto = review.TargetEntityId
        });
    }

    private static async Task<IResult> Reject(
        Guid id,
        SentinelMapDbContext db,
        IUserContext userContext,
        CancellationToken ct)
    {
        var review = await db.CorrelationReviews.FindAsync([id], ct);
        if (review is null) return Results.NotFound();
        if (review.Status != "Pending")
            return Results.Conflict(new { message = $"Review is already {review.Status}." });

        review.Status = "Rejected";
        review.ReviewedBy = userContext.UserId;
        review.ReviewedAt = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(ct);

        return Results.Ok(new
        {
            review.Id,
            review.Status,
            review.ReviewedBy,
            review.ReviewedAt
        });
    }
}

record CorrelationReviewResponse(
    Guid Id,
    Guid SourceEntityId, string? SourceName, string? SourceType,
    Guid TargetEntityId, string? TargetName, string? TargetType,
    double Confidence, string? RuleScores, string Status,
    DateTimeOffset CreatedAt);
