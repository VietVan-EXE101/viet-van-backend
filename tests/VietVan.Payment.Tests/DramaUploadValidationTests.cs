using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using VietVan.API.Controllers.Admin;
using VietVan.API.Services.Implementations;
using Xunit;

public class DramaUploadValidationTests
{
    private static IFormFile MakeFile(string filename, string type, long size) =>
        new FormFile(Stream.Null, 0, size, "file", filename) { Headers = new HeaderDictionary(), ContentType = type };

    [Theory]
    [InlineData("Background", ".jpg", "image/jpeg", 10)]
    [InlineData("Object", ".png", "image/png", 10)]
    [InlineData("Character", ".webp", "image/webp", 10)]
    [InlineData("Character", ".gif", "image/gif", 10)]
    [InlineData("Object", ".gif", "image/gif", 10)]
    [InlineData("Background", ".gif", "image/gif", 10)]
    [InlineData("Effect", ".gif", "image/gif", 10)]
    [InlineData("Audio", ".mp3", "audio/mpeg", 30)]
    [InlineData("Video", ".mp4", "video/mp4", 200)]
    public void FormatAndSizeAreValidated(string kind, string ext, string mime, int maxMiB)
    {
        var limit = maxMiB * 1024L * 1024;
        DramaService.ValidateUpload(MakeFile("file" + ext, mime, limit), kind, ext);
        Assert.Throws<ArgumentException>(() => DramaService.ValidateUpload(MakeFile("file" + ext, mime, limit + 1), kind, ext));
        Assert.Throws<ArgumentException>(() => DramaService.ValidateUpload(MakeFile("file" + ext, "text/html", 100), kind, ext));
    }

    [Fact]
    public void KindAndExtensionAreChecked()
    {
        Assert.Throws<ArgumentException>(() => DramaService.ValidateUpload(MakeFile("file.exe", "application/octet-stream", 12), "Video", ".exe"));
        Assert.Throws<ArgumentException>(() => DramaService.ValidateUpload(MakeFile("file.png", "image/png", 12), "Audio", ".png"));
        Assert.Contains(typeof(AdminDramasController).GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>(), a => a.Roles == "Admin");
    }

    [Theory]
    [InlineData(".jpg", "FFD8FF0000")]
    [InlineData(".png", "89504E470D0A1A0A")]
    [InlineData(".webp", "524946460000000057454250")]
    [InlineData(".gif", "474946383761")]
    [InlineData(".gif", "474946383961")]
    [InlineData(".mp3", "4944330000")]
    [InlineData(".mp4", "0000001066747970")]
    public void SignatureCheckRejectsWrongBytes(string ext, string hex)
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(path, Convert.FromHexString(hex));
            Assert.True(DramaService.HasExpectedSignature(path, ext));
            File.WriteAllBytes(path, new byte[16]);
            Assert.False(DramaService.HasExpectedSignature(path, ext));
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData("474946")]
    [InlineData("4749463839")]
    [InlineData("474946383962")]
    [InlineData("474946383861")]
    [InlineData("676966383961")]
    public void GifSignatureRequiresCompleteSupportedHeader(string hex)
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(path, Convert.FromHexString(hex));
            Assert.False(DramaService.HasExpectedSignature(path, ".gif"));
        }
        finally { File.Delete(path); }
    }
}
