using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.Swagger;
using VietVan.API.Controllers.Admin;
using VietVan.API.DTOs.Works;
using VietVan.API.Entities;
using VietVan.API.Services.Implementations;
using VietVan.API.Services.Interfaces;
using Xunit;

public class WorkCoverTests
{
    private sealed class Env(string root) : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = typeof(AdminWorksController).Assembly.GetName().Name!;
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = root;
        public string EnvironmentName { get; set; } = "Development";
        public string ContentRootPath { get; set; } = root;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
    private sealed class Service : IWorkService
    {
        public bool Fail { get; set; }
        public CreateWorkRequest? Saved { get; private set; }
        public Task<LiteraryWork> CreateAsync(CreateWorkRequest r)
        {
            if (Fail) throw new InvalidOperationException("database error");
            Saved = r;
            return Task.FromResult(new LiteraryWork { Id = 42, Title = r.Title, CoverImageUrl = r.CoverImageUrl });
        }
        public Task<PagedResponse<WorkListItem>> GetPublicAsync(int page, int pageSize, string? search) => throw new NotImplementedException();
        public Task<LiteraryWork> ToggleAsync(int id) => throw new NotImplementedException();
    }
    private static FormFile File(byte[] data, string name, string mime) => new(new MemoryStream(data), 0, data.Length, "coverImage", name)
    { Headers = new HeaderDictionary(), ContentType = mime };

    [Fact]
    public async Task CreatesWithAndWithoutCoverAndRejectsInvalidAndCleansOnFailure()
    {
        var root = Path.Combine(Path.GetTempPath(), "vietvan-cover-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var service = new Service();
            var controller = new AdminWorksController(service, new WorkCoverStorage(new Env(root)), NullLogger<AdminWorksController>.Instance);
            var png = new byte[] { 0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a, 1, 2, 3, 4 };
            var result = await controller.CreateWithCover(new CreateWorkFormRequest { Title = "Test", CoverImage = File(png, "cover.png", "image/png") }, default);
            Assert.IsType<CreatedResult>(result);
            Assert.StartsWith("/uploads/work-covers/", service.Saved!.CoverImageUrl);
            var directory = Path.Combine(root, "uploads", "work-covers");
            Assert.Single(Directory.GetFiles(directory));

            result = await controller.CreateWithCover(new CreateWorkFormRequest { Title = "No image" }, default);
            Assert.IsType<CreatedResult>(result);
            Assert.Null(service.Saved!.CoverImageUrl);
            result = await controller.CreateWithCover(new CreateWorkFormRequest { CoverImage = File(new byte[12], "bad.png", "image/png") }, default);
            Assert.IsType<BadRequestObjectResult>(result);
            Assert.Single(Directory.GetFiles(directory));

            service.Fail = true;
            await Assert.ThrowsAsync<InvalidOperationException>(() => controller.CreateWithCover(new CreateWorkFormRequest { CoverImage = File(png, "cover.png", "image/png") }, default));
            Assert.Single(Directory.GetFiles(directory));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task RejectsOversizedFile()
    {
        var root = Path.GetTempPath();
        var file = File(new byte[WorkCoverStorage.MaxSize + 1], "big.png", "image/png");
        await Assert.ThrowsAsync<ArgumentException>(() => new WorkCoverStorage(new Env(root)).SaveAsync(file, default));
    }

    [Theory]
    [InlineData("cover.webp", "image/webp", "524946460400000057454250")]
    [InlineData("cover.jpg", "image/jpeg", "FFD8FF000000000000000000")]
    [InlineData("cover.png", "image/png", "89504E470D0A1A0A00000000")]
    public async Task AcceptsSupportedCoverSignatures(string filename, string contentType, string signature)
    {
        var root = Path.Combine(Path.GetTempPath(), "vietvan-cover-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var service = new Service();
            var controller = new AdminWorksController(service, new WorkCoverStorage(new Env(root)), NullLogger<AdminWorksController>.Instance);
            var result = await controller.CreateWithCover(new CreateWorkFormRequest
            {
                Title = "Test", CoverImage = File(Convert.FromHexString(signature), filename, contentType)
            }, default);
            var created = Assert.IsType<CreatedResult>(result);
            Assert.Equal(201, created.StatusCode);
            Assert.Equal(42, Assert.IsType<WorkMetadataDto>(created.Value).Id);
            Assert.True(System.IO.File.Exists(Path.Combine(root, service.Saved!.CoverImageUrl!.TrimStart('/').Replace('/', Path.DirectorySeparatorChar))));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void SwaggerDocumentsBothJsonAndFileUpload()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IWebHostEnvironment>(new Env(Path.GetTempPath()));
        services.AddLogging();
        services.AddControllers().AddApplicationPart(typeof(AdminWorksController).Assembly);
        services.AddSwaggerGen(o => { o.SwaggerDoc("v1", new OpenApiInfo { Title = "Test", Version = "v1" }); o.OperationFilter<WorkCreateSwaggerFilter>(); });
        using var provider = services.BuildServiceProvider();
        var doc = provider.GetRequiredService<ISwaggerProvider>().GetSwagger("v1");
        var operation = doc.Paths["/api/admin/works"].Operations[OperationType.Post];
        Assert.Contains("201", operation.Responses.Keys);
        var request = operation.RequestBody;
        Assert.Contains("multipart/form-data", request.Content.Keys);
        Assert.Contains("application/json", request.Content.Keys);
        var properties = request.Content["multipart/form-data"].Schema.Properties;
        Assert.True(properties.TryGetValue("coverImage", out var file));
        Assert.Equal("binary", file!.Format);
        var dramaOperation = doc.Paths["/api/admin/dramas"].Operations[OperationType.Post];
        Assert.Contains("200", dramaOperation.Responses.Keys);
        Assert.Contains("400", dramaOperation.Responses.Keys);
        Assert.Contains("503", dramaOperation.Responses.Keys);
        var drama = dramaOperation.RequestBody;
        Assert.Contains("application/json", drama.Content.Keys);
        Assert.True(drama.Content["multipart/form-data"].Schema.Properties.TryGetValue("coverImage", out var dramaFile));
        Assert.Equal("binary", dramaFile!.Format);
    }
}
