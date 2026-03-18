using Microsoft.AspNetCore.Authorization;
using SentinelMap.SharedKernel.Interfaces;

namespace SentinelMap.Infrastructure.Auth;

public class ClassificationRequirement : IAuthorizationRequirement { }

public class ClassificationAuthorizationHandler : AuthorizationHandler<ClassificationRequirement>
{
    private readonly IUserContext _userContext;

    public ClassificationAuthorizationHandler(IUserContext userContext)
    {
        _userContext = userContext;
    }

    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        ClassificationRequirement requirement)
    {
        if (_userContext.IsAuthenticated)
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
