using SentinelMap.SharedKernel.Enums;

namespace SentinelMap.Domain.Entities;

/// <summary>
/// Domain projection of Identity's AspNetUsers.
/// Identity handles auth; this table handles domain concerns.
/// </summary>
public class User
{
    public Guid Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
    public string Role { get; set; } = Roles.Viewer;
    public Classification ClearanceLevel { get; set; } = Classification.Official;
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
