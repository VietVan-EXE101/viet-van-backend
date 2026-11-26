using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using VietVan.API.Services.Storage;

namespace VietVan.API.Hosting;

public static class R2SmokeCheck
{
    public static async Task<int> RunAsync(IConfiguration configuration, IWebHostEnvironment environment, TextWriter output)
    {
        // Chỉ lệnh người vận hành gọi tường minh. Key riêng, không đụng file của người dùng.
        var key = $"pro/assets/_diagnostics/{Guid.NewGuid():N}.gif";
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".gif");
        R2ObjectClient? objects = null;
        var step = "CONFIGURATION";
        try
        {
            var effective = MediaStorageConfiguration.Effective(configuration, environment);
            var options = effective.GetSection("R2").Get<R2Options>() ?? new();
            var invalid = options.InvalidKeys();
            if (invalid.Count > 0)
            {
                await output.WriteLineAsync("R2 configuration invalid: " + string.Join(", ", invalid) + ". Chỉ in tên cấu hình, không in giá trị.");
                return 1;
            }
            step = "CLIENT_INIT";
            using var sdk = R2ObjectClient.Create(options);
            objects = new R2ObjectClient(sdk, Options.Create(options));
            step = "TEMP_FILE";
            var bytes = Convert.FromHexString("47494638396101000100800000000000FFFFFF21F904000A0000002C00000000010001000002024401003B");
            await File.WriteAllBytesAsync(path, bytes);
            var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            var ownsObject = false;
            try
            {
                step = "UPLOAD";
                if (!await objects.PutFileAsync(key, path, "image/gif", hash, default)) throw new InvalidOperationException();
                ownsObject = true;
                await output.WriteLineAsync("PASS upload object thử mới");
                step = "HEAD_AND_HASH";
                var remote = await objects.HeadAsync(key, default);
                if (remote?.Size != bytes.Length || await objects.ReadHashAsync(key, default) != hash) throw new InvalidOperationException();
                await output.WriteLineAsync("PASS HEAD và SHA256 byte gốc");
                step = "PRESIGN";
                var url = await objects.PresignGetAsync(key, DateTimeOffset.UtcNow.AddMinutes(1));
                step = "RANGE_GET";
                using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false });
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Range = new RangeHeaderValue(0, 5);
                using var response = await client.SendAsync(request);
                if (response.StatusCode != HttpStatusCode.PartialContent || response.Content.Headers.ContentType?.MediaType != "image/gif" ||
                    !(await response.Content.ReadAsByteArrayAsync()).SequenceEqual(bytes.Take(6)))
                    throw new MediaStorageUnavailableException((int)response.StatusCode, "RANGE_RESPONSE_INVALID");
                await output.WriteLineAsync("PASS presigned GET và HTTP Range 206; không in signed URL");
            }
            // Nếu key đã tồn tại, không xóa object không thuộc lần thử này.
            finally
            {
                if (ownsObject)
                {
                    var previousStep = step;
                    step = "DELETE";
                    await objects.DeleteAsync(key, default);
                    step = previousStep;
                }
            }
            step = "VERIFY_DELETE";
            if (await objects.HeadAsync(key, default) is not null) throw new InvalidOperationException();
            await output.WriteLineAsync("PASS xóa object thử. Không sửa database/media hiện có.");
            return 0;
        }
        catch (Exception ex)
        {
            var detail = ex is MediaStorageUnavailableException storage
                ? $" Code={storage.FailureCode}; HTTP={storage.HttpStatus?.ToString() ?? "none"}." : "";
            await output.WriteLineAsync($"R2 check failed: Step={step}; Type={ex.GetType().Name}.{detail} Không in secret/signed URL. Kiểm tra cấu hình, quyền bucket và kết nối HTTPS.");
            return 1;
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
}
