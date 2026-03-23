using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SentinelMap.Domain.Entities;
using SentinelMap.Infrastructure.Data;
using SentinelMap.SharedKernel.Interfaces;

namespace SentinelMap.Api.Endpoints;

public static class WebhookEndpoints
{
    public static void MapWebhookEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/webhooks").WithTags("Webhooks");

        group.MapGet("/", ListEndpoints).RequireAuthorization("AdminAccess");
        group.MapPost("/", CreateEndpoint).RequireAuthorization("AdminAccess");
        group.MapPut("/{id:guid}", UpdateEndpoint).RequireAuthorization("AdminAccess");
        group.MapDelete("/{id:guid}", DeleteEndpoint).RequireAuthorization("AdminAccess");
        group.MapPost("/{id:guid}/test", TestEndpoint).RequireAuthorization("AdminAccess");
    }

    record CreateWebhookRequest(string Url, string Secret, string? EventFilter = null);
    record UpdateWebhookRequest(string? Url = null, string? Secret = null, string? EventFilter = null, bool? IsActive = null);

    private static async Task<IResult> ListEndpoints(
        SentinelMapDbContext db,
        CancellationToken ct)
    {
        var endpoints = await db.WebhookEndpoints.OrderByDescending(e => e.CreatedAt).ToListAsync(ct);
        return Results.Ok(endpoints.Select(e => new
        {
            e.Id,
            e.Url,
            e.EventFilter,
            e.IsActive,
            e.ConsecutiveFailures,
            e.CreatedBy,
            e.CreatedAt
        }));
    }

    private static async Task<IResult> CreateEndpoint(
        CreateWebhookRequest request,
        SentinelMapDbContext db,
        IUserContext userContext,
        CancellationToken ct)
    {
        var endpoint = new WebhookEndpoint
        {
            Url = request.Url,
            Secret = request.Secret,
            EventFilter = request.EventFilter,
            CreatedBy = userContext.UserId ?? Guid.Empty
        };

        db.WebhookEndpoints.Add(endpoint);
        await db.SaveChangesAsync(ct);

        return Results.Created($"/api/v1/webhooks/{endpoint.Id}", new
        {
            endpoint.Id,
            endpoint.Url,
            endpoint.EventFilter,
            endpoint.IsActive,
            endpoint.CreatedAt
        });
    }

    private static async Task<IResult> UpdateEndpoint(
        Guid id,
        UpdateWebhookRequest request,
        SentinelMapDbContext db,
        CancellationToken ct)
    {
        var endpoint = await db.WebhookEndpoints.FindAsync([id], ct);
        if (endpoint is null) return Results.NotFound();

        if (request.Url is not null) endpoint.Url = request.Url;
        if (request.Secret is not null) endpoint.Secret = request.Secret;
        if (request.EventFilter is not null) endpoint.EventFilter = request.EventFilter;
        if (request.IsActive.HasValue) endpoint.IsActive = request.IsActive.Value;

        await db.SaveChangesAsync(ct);

        return Results.Ok(new
        {
            endpoint.Id,
            endpoint.Url,
            endpoint.EventFilter,
            endpoint.IsActive,
            endpoint.ConsecutiveFailures,
            endpoint.CreatedAt
        });
    }

    private static async Task<IResult> DeleteEndpoint(
        Guid id,
        SentinelMapDbContext db,
        CancellationToken ct)
    {
        var endpoint = await db.WebhookEndpoints.FindAsync([id], ct);
        if (endpoint is null) return Results.NotFound();

        db.WebhookEndpoints.Remove(endpoint);
        await db.SaveChangesAsync(ct);

        return Results.NoContent();
    }

    private static async Task<IResult> TestEndpoint(
        Guid id,
        SentinelMapDbContext db,
        IHttpClientFactory httpClientFactory,
        CancellationToken ct)
    {
        var endpoint = await db.WebhookEndpoints.FindAsync([id], ct);
        if (endpoint is null) return Results.NotFound();

        var testPayload = JsonSerializer.Serialize(new
        {
            alertId = Guid.NewGuid(),
            type = "Test",
            severity = "Low",
            entityId = (Guid?)null,
            summary = "Test webhook delivery from SentinelMap",
            createdAt = DateTimeOffset.UtcNow
        });

        var signature = ComputeHmacSha256(endpoint.Secret, testPayload);

        var client = httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(10);

        var request = new HttpRequestMessage(HttpMethod.Post, endpoint.Url)
        {
            Content = new StringContent(testPayload, Encoding.UTF8, "application/json")
        };
        request.Headers.Add("X-Signature-256", $"sha256={signature}");

        try
        {
            var sw = Stopwatch.StartNew();
            var response = await client.SendAsync(request, ct);
            sw.Stop();

            return Results.Ok(new
            {
                success = response.IsSuccessStatusCode,
                statusCode = (int)response.StatusCode,
                latencyMs = (int)sw.ElapsedMilliseconds
            });
        }
        catch (Exception ex)
        {
            return Results.Ok(new
            {
                success = false,
                statusCode = (int?)null,
                latencyMs = (int?)null,
                error = ex.Message
            });
        }
    }

    private static string ComputeHmacSha256(string secret, string payload)
    {
        var key = Encoding.UTF8.GetBytes(secret);
        var data = Encoding.UTF8.GetBytes(payload);
        var hash = HMACSHA256.HashData(key, data);
        return Convert.ToHexStringLower(hash);
    }
}
