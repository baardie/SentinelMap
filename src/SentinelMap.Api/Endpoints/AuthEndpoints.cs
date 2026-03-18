namespace SentinelMap.Api.Endpoints;

public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/auth").WithTags("Auth");

        group.MapPost("/login", Login).AllowAnonymous();
        group.MapPost("/refresh", Refresh).AllowAnonymous();
        group.MapPost("/revoke", Revoke).RequireAuthorization("ViewerAccess");
    }

    private static async Task<IResult> Login()
    {
        // TODO (M1 continued): Implement login with Identity + JWT
        return Results.Ok(new { message = "Login endpoint — implementation pending" });
    }

    private static async Task<IResult> Refresh()
    {
        return Results.Ok(new { message = "Refresh endpoint — implementation pending" });
    }

    private static async Task<IResult> Revoke()
    {
        return Results.Ok(new { message = "Revoke endpoint — implementation pending" });
    }
}
