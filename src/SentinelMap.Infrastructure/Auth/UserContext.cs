using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using SentinelMap.SharedKernel.Enums;
using SentinelMap.SharedKernel.Interfaces;

namespace SentinelMap.Infrastructure.Auth;

public class UserContext : IUserContext
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public UserContext(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    private ClaimsPrincipal? User => _httpContextAccessor.HttpContext?.User;

    public bool IsAuthenticated => User?.Identity?.IsAuthenticated ?? false;

    public Guid? UserId
    {
        get
        {
            var sub = User?.FindFirstValue(ClaimTypes.NameIdentifier);
            return Guid.TryParse(sub, out var id) ? id : null;
        }
    }

    public string? Email => User?.FindFirstValue(ClaimTypes.Email);

    public string? Role => User?.FindFirstValue(ClaimTypes.Role);

    public Classification ClearanceLevel
    {
        get
        {
            var clearance = User?.FindFirstValue("clearance");
            return clearance switch
            {
                "Secret" => Classification.Secret,
                "OfficialSensitive" => Classification.OfficialSensitive,
                _ => Classification.Official
            };
        }
    }
}
