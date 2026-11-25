using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;
using VietVan.API.Controllers.Admin;
using VietVan.API.DTOs.Works;
using VietVan.API.DTOs.Drama;

namespace VietVan.API.Services.Implementations;

public sealed class WorkCreateSwaggerFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        var requestType = context.MethodInfo.DeclaringType == typeof(AdminWorksController) &&
            context.MethodInfo.Name == nameof(AdminWorksController.CreateWithCover) ? typeof(CreateWorkRequest) :
            context.MethodInfo.DeclaringType == typeof(AdminDramasController) &&
            context.MethodInfo.Name == nameof(AdminDramasController.CreateFromForm) ? typeof(DramaRequest) : null;
        if (requestType is null) return;
        operation.RequestBody.Content["application/json"] = new OpenApiMediaType
        {
            Schema = context.SchemaGenerator.GenerateSchema(requestType, context.SchemaRepository)
        };
    }
}
