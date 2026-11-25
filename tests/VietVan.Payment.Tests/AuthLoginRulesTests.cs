using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using VietVan.API.Entities;
using VietVan.API.Services.Implementations;
using Xunit;

public class AuthLoginRulesTests
{
    [Theory]
    [InlineData("Admin", false, false, true)]
    [InlineData("Admin", false, true, false)]
    [InlineData("Admin", true, true, false)]
    [InlineData("Customer", false, false, false)]
    [InlineData("Customer", true, false, true)]
    [InlineData("Customer", true, true, false)]
    [InlineData("admin", false, false, false)]
    public void LoginStateRules(string role, bool verified, bool locked, bool allowed)
    {
        var user = new User { Role = role };
        var state = new UserAuthState { EmailVerified = verified, IsLocked = locked };
        var error = Record.Exception(() => AuthService.EnsureLoginAllowed(user, state));
        if (allowed) Assert.Null(error);
        else Assert.IsType<UnauthorizedAccessException>(error);
    }

    [Theory]
    [InlineData("Customer", false)]
    [InlineData("Admin", true)]
    public async Task AdminAuthorizationRequiresAdminRole(string role, bool allowed)
    {
        var services = new ServiceCollection().AddLogging().AddAuthorization().BuildServiceProvider();
        var authorization = services.GetRequiredService<IAuthorizationService>();
        var principal = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Role, role) }, "test"));
        var result = await authorization.AuthorizeAsync(principal, null,
            new AuthorizationPolicyBuilder().RequireRole("Admin").Build());
        Assert.Equal(allowed, result.Succeeded);
    }
}
