using SentinelMap.SharedKernel.Enums;

namespace SentinelMap.SharedKernel.Interfaces;

public interface IUserContext
{
    Guid? UserId { get; }
    string? Email { get; }
    string? Role { get; }
    Classification ClearanceLevel { get; }
    bool IsAuthenticated { get; }
}
