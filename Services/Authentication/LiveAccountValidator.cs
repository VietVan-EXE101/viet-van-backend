using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using VietVan.API.Data;

namespace VietVan.API.Services.Authentication;

public static class LiveAccountValidator
{
    public static async Task ValidateAsync(TokenValidatedContext context)
    {
        var principal = context.Principal!;
        if (!int.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var id)) { context.Fail("Invalid account"); return; }
        var db = context.HttpContext.RequestServices.GetRequiredService<VietVanDbContext>();
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, context.HttpContext.RequestAborted);
        var state = await db.UserAuthStates.AsNoTracking().SingleOrDefaultAsync(x => x.UserId == id, context.HttpContext.RequestAborted);
        var versionClaim = principal.FindFirstValue("token_version");
        var version = versionClaim is null ? 0 : int.TryParse(versionClaim, out var n) ? n : -1;
        if (user is null || state?.IsLocked == true || version != (state?.TokenVersion ?? 0) ||
            (state?.EmailVerified != true && user.Role != "Admin") || user.Role is not ("Admin" or "Customer"))
        { context.Fail("Account disabled or session revoked"); return; }
        // Role đang có trong DB thay thế role cũ trên JWT, không tin claim quyền truy cập PRO.
        var identity = (ClaimsIdentity)principal.Identity!;
        foreach (var role in identity.FindAll(identity.RoleClaimType).ToList()) identity.RemoveClaim(role);
        identity.AddClaim(new Claim(identity.RoleClaimType, user.Role));
    }
}
