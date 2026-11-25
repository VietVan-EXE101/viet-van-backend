using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Collections.Concurrent;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using VietVan.API.Filters;
using Xunit;

namespace VietVan.Payment.Tests;

[ApiController, Route("upload-transport-probe")]
public class UploadTransportProbeController : ControllerBase
{
    [HttpPost("upload"), UploadRequest, RequestSizeLimit(1024)]
    public IActionResult Upload([FromForm] IFormFile file) => Ok(new
    {
        length = file.Length,
        limit = HttpContext.Features.Get<IHttpMaxRequestBodySizeFeature>()?.MaxRequestBodySize
    });

    [HttpPost("control"), RequestSizeLimit(1024)]
    public IActionResult Control([FromForm] IFormFile file) => Ok(file.Length);
}

public class UploadTransportTests
{
    private sealed class CapturedLogs : ILoggerProvider
    {
        public ConcurrentQueue<string> Messages { get; } = new();
        public ILogger CreateLogger(string categoryName) => new Capture(Messages);
        public void Dispose() { }
        private sealed class Capture(ConcurrentQueue<string> messages) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
                messages.Enqueue(eventId.Name + " " + formatter(state, exception) + " " + exception?.Message);
        }
    }

    private sealed class PausedMultipartContent : HttpContent
    {
        private readonly byte[] body = Encoding.ASCII.GetBytes(
            "--slow-boundary\r\nContent-Disposition: form-data; name=\"file\"; filename=\"cover.webp\"\r\n" +
            "Content-Type: image/webp\r\n\r\nRIFFxxxxWEBP\r\n--slow-boundary--\r\n");

        public PausedMultipartContent()
        {
            Headers.ContentType = new MediaTypeHeaderValue("multipart/form-data");
            Headers.ContentType.Parameters.Add(new NameValueHeaderValue("boundary", "slow-boundary"));
        }

        protected override bool TryComputeLength(out long length) { length = body.Length; return true; }
        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        {
            await stream.WriteAsync(body.AsMemory(0, 12));
            await stream.FlushAsync();
            await Task.Delay(TimeSpan.FromSeconds(4));
            await stream.WriteAsync(body.AsMemory(12));
        }
    }

    [Fact]
    public async Task SlowMultipartPassesBeforeModelBindingWhileOtherRoutesAndSizeCapsStayProtected()
    {
        var logs = new CapturedLogs();
        var builder = WebApplication.CreateBuilder(); builder.Logging.ClearProviders();
        builder.Logging.SetMinimumLevel(LogLevel.Debug); builder.Logging.AddProvider(logs);
        builder.Logging.AddFilter("Microsoft.AspNetCore.Server.Kestrel", LogLevel.Debug);
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        // A short grace period makes the original failure reproducible in a few seconds.
        builder.WebHost.ConfigureKestrel(o => o.Limits.MinRequestBodyDataRate = new MinDataRate(10000, TimeSpan.FromSeconds(2)));
        builder.Services.AddControllers().AddApplicationPart(typeof(UploadTransportProbeController).Assembly);
        await using var app = builder.Build(); app.MapControllers(); await app.StartAsync();
        try
        {
            var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            using var client = new HttpClient { BaseAddress = new Uri(address), Timeout = TimeSpan.FromSeconds(20) };
            using var uploadRequest = new HttpRequestMessage(HttpMethod.Post, "/upload-transport-probe/upload") { Content = new PausedMultipartContent() };
            using var controlRequest = new HttpRequestMessage(HttpMethod.Post, "/upload-transport-probe/control") { Content = new PausedMultipartContent() };
            async Task<(HttpResponseMessage? Response, HttpRequestException? Failure)> SendControlAsync()
            {
                try { return (await client.SendAsync(controlRequest), null); }
                catch (HttpRequestException ex) { return (null, ex); }
            }
            var controlTask = SendControlAsync();
            using var upload = await client.SendAsync(uploadRequest);
            var control = await controlTask;
            using var controlResponse = control.Response;
            // Kestrel may send MVC's 400 or abort the socket immediately on Windows.
            Assert.True(control.Failure is not null || controlResponse?.StatusCode == HttpStatusCode.BadRequest);
            Assert.True(logs.Messages.Any(message => message.Contains("MinRequestBodyDataRate", StringComparison.OrdinalIgnoreCase)
                || message.Contains("MinimumDataRate", StringComparison.OrdinalIgnoreCase)
                || message.Contains("minimum data rate", StringComparison.OrdinalIgnoreCase)
                || message.Contains("data arriving too slowly", StringComparison.OrdinalIgnoreCase)), string.Join(Environment.NewLine, logs.Messages));
            Assert.Equal(HttpStatusCode.OK, upload.StatusCode);
            using var result = System.Text.Json.JsonDocument.Parse(await upload.Content.ReadAsStringAsync());
            Assert.Equal(12, result.RootElement.GetProperty("length").GetInt64());
            Assert.Equal(1024, result.RootElement.GetProperty("limit").GetInt64());

            using var oversized = new MultipartFormDataContent();
            oversized.Add(new ByteArrayContent(new byte[2048]), "file", "large.webp");
            using var rejected = await client.PostAsync("/upload-transport-probe/upload", oversized);
            Assert.True(rejected.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.RequestEntityTooLarge);
        }
        finally { await app.StopAsync(); }
    }
}
