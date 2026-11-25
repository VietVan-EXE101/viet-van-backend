using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VietVan.API.Controllers.Admin;
using Xunit;

public class DramaCreateFormTests
{
    [Fact]
    public void CreateRoutesAcceptFormAndRetainJsonUnderAdminAuthorization()
    {
        var controller = typeof(AdminDramasController);
        Assert.Equal("Admin", Assert.Single(controller.GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>()).Roles);
        var form = controller.GetMethod(nameof(AdminDramasController.CreateFromForm))!;
        var json = controller.GetMethod(nameof(AdminDramasController.Create))!;
        Assert.Contains("multipart/form-data", Assert.Single(form.GetCustomAttributes(typeof(ConsumesAttribute), true).Cast<ConsumesAttribute>()).ContentTypes);
        Assert.Contains("application/json", Assert.Single(json.GetCustomAttributes(typeof(ConsumesAttribute), true).Cast<ConsumesAttribute>()).ContentTypes);
        Assert.NotEmpty(form.GetParameters()[0].GetCustomAttributes(typeof(FromFormAttribute), true));
        Assert.NotEmpty(form.GetCustomAttributes(typeof(VietVan.API.Filters.UploadRequestAttribute), true));
        Assert.NotEmpty(json.GetParameters()[0].GetCustomAttributes(typeof(FromBodyAttribute), true));
        Assert.True(Assert.Single(json.GetCustomAttributes(typeof(ApiExplorerSettingsAttribute), true).Cast<ApiExplorerSettingsAttribute>()).IgnoreApi);
    }
}
