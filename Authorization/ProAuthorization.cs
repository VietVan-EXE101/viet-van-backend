using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using VietVan.API.Services.Payments;

namespace VietVan.API.Authorization;

public sealed class ProRequirement : IAuthorizationRequirement;

public sealed class ProAuthorizationHandler(EntitlementService entitlements) : AuthorizationHandler<ProRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, ProRequirement requirement)
    {
        if (context.User.Identity?.IsAuthenticated != true) return;
        if (context.User.IsInRole("Admin")) { context.Succeed(requirement); return; }
        if (int.TryParse(context.User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) && await entitlements.CurrentTierAsync(id) > 0)
            context.Succeed(requirement);
    }
}
