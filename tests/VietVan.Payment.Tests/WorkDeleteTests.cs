using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using VietVan.API.Controllers.Admin;
using Xunit;

public class WorkDeleteTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task InvalidIdsAreRejectedBeforeDatabaseAccess(int id)
    {
        var controller = new AdminWorksController(null!, null!, NullLogger<AdminWorksController>.Instance);
        Assert.IsType<BadRequestObjectResult>(await controller.Delete(id, null!, CancellationToken.None));
    }

    [Fact]
    public void DeleteIsExposedOnAdminOnlyController()
    {
        var authorization = Assert.Single(typeof(AdminWorksController).GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>());
        Assert.Equal("Admin", authorization.Roles);
        var method = typeof(AdminWorksController).GetMethod(nameof(AdminWorksController.Delete))!;
        var route = Assert.Single(method.GetCustomAttributes(typeof(HttpDeleteAttribute), true).Cast<HttpDeleteAttribute>());
        Assert.Equal("{id:int}", route.Template);
        Assert.DoesNotContain(method.GetCustomAttributes(true), x => x is ApiExplorerSettingsAttribute settings && settings.IgnoreApi);
    }
}
