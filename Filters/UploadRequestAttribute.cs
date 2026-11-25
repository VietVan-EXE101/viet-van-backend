using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.Server.Kestrel.Core.Features;

namespace VietVan.API.Filters;

/// <summary>Allow proxy-buffered uploads to start slowly, before MVC reads the form.</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class UploadRequestAttribute : Attribute, IResourceFilter
{
    private static readonly MinDataRate UploadDataRate = new(64, TimeSpan.FromSeconds(60));

    public void OnResourceExecuting(ResourceExecutingContext context)
    {
        // Kestrel permits non-null per-request rate overrides only on HTTP/1.x.
        // ngrok forwards to the local HTTP listener using HTTP/1.1. HTTP/2 keeps
        // the server policy; setting a non-null rate there would throw.
        var request = context.HttpContext.Request;
        if (!HttpProtocol.IsHttp10(request.Protocol) && !HttpProtocol.IsHttp11(request.Protocol)) return;
        var feature = context.HttpContext.Features.Get<IHttpMinRequestBodyDataRateFeature>();
        if (feature is not null) feature.MinDataRate = UploadDataRate;
    }

    public void OnResourceExecuted(ResourceExecutedContext context) { }
}
