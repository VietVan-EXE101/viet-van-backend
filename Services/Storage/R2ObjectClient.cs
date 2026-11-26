using System.Net;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Options;

namespace VietVan.API.Services.Storage;

public sealed record RemoteObject(long Size, string ContentType, string? Sha256);

public interface IR2ObjectClient
{
    Task<RemoteObject?> HeadAsync(string key, CancellationToken ct);
    Task<bool> PutFileAsync(string key, string path, string contentType, string sha256, CancellationToken ct);
    Task DeleteAsync(string key, CancellationToken ct);
    Task<string> PresignGetAsync(string key, DateTimeOffset expiresAt);
    Task<string> ReadHashAsync(string key, CancellationToken ct);
}

public sealed class R2ObjectClient(IAmazonS3 s3, IOptions<R2Options> options) : IR2ObjectClient
{
    private readonly string bucket = options.Value.BucketName;

    internal static MediaStorageUnavailableException Unavailable(Exception error)
    {
        var service = error as AmazonServiceException;
        var code = service?.ErrorCode switch
        {
            "AccessDenied" => "ACCESS_DENIED",
            "InvalidAccessKeyId" => "INVALID_ACCESS_KEY",
            "SignatureDoesNotMatch" => "SIGNATURE_MISMATCH",
            "NoSuchBucket" => "BUCKET_NOT_FOUND",
            "RequestTimeTooSkewed" or "RequestExpired" => "CLOCK_SKEW",
            _ => "STORAGE_UNAVAILABLE"
        };
        // Chỉ giữ mã đã cho phép; không giữ message/inner exception có thể chứa URL hoặc credential.
        for (Exception? current = error; current is not null; current = current.InnerException)
        {
            if (current is System.Security.Authentication.AuthenticationException) { code = "TLS_ERROR"; break; }
            if (current is HttpRequestException or System.Net.Sockets.SocketException) code = "NETWORK_ERROR";
        }
        var status = (int?)service?.StatusCode;
        return new(status is > 0 ? status : null, code);
    }

    public static AmazonS3Client Create(R2Options options)
    {
        var endpoint = options.Endpoint();
        return new AmazonS3Client(new BasicAWSCredentials(options.AccessKeyId, options.SecretAccessKey), new AmazonS3Config
        {
            ServiceURL = endpoint.AbsoluteUri.TrimEnd('/'), AuthenticationRegion = "auto", ForcePathStyle = true,
            // R2 không hỗ trợ streaming checksum trailer mặc định của SDK mới.
            RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED,
            ResponseChecksumValidation = ResponseChecksumValidation.WHEN_REQUIRED,
            Timeout = TimeSpan.FromMinutes(5), MaxErrorRetry = 2
        });
    }

    public async Task<RemoteObject?> HeadAsync(string key, CancellationToken ct)
    {
        try
        {
            var result = await s3.GetObjectMetadataAsync(new GetObjectMetadataRequest { BucketName = bucket, Key = R2Options.Key(key) }, ct);
            var sha256 = result.Metadata.Keys.Contains("x-amz-meta-sha256") ? result.Metadata["x-amz-meta-sha256"] : null;
            return new(result.Headers.ContentLength, result.Headers.ContentType ?? "application/octet-stream", sha256);
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound) { return null; }
        catch (Exception ex) when (ex is AmazonServiceException or AmazonClientException or HttpRequestException) { throw Unavailable(ex); }
    }

    public async Task<bool> PutFileAsync(string key, string path, string contentType, string sha256, CancellationToken ct)
    {
        try
        {
            // FilePath streaming từ file tạm; không nạp video vào RAM, không ghi đè object đã có.
            var request = new PutObjectRequest
            {
                BucketName = bucket, Key = R2Options.Key(key), FilePath = path, ContentType = contentType,
                IfNoneMatch = "*", UseChunkEncoding = false,
                DisablePayloadSigning = true, DisableDefaultChecksumValidation = true
            };
            request.Headers.CacheControl = "private, no-store";
            request.Metadata["sha256"] = sha256;
            await s3.PutObjectAsync(request, ct);
            return true;
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.PreconditionFailed) { return false; }
        catch (Exception ex) when (ex is AmazonServiceException or AmazonClientException or HttpRequestException) { throw Unavailable(ex); }
    }

    public async Task DeleteAsync(string key, CancellationToken ct)
    {
        try { await s3.DeleteObjectAsync(new DeleteObjectRequest { BucketName = bucket, Key = R2Options.Key(key) }, ct); }
        catch (Exception ex) when (ex is AmazonServiceException or AmazonClientException or HttpRequestException) { throw Unavailable(ex); }
    }

    public async Task<string> PresignGetAsync(string key, DateTimeOffset expiresAt)
    {
        try
        {
            return await s3.GetPreSignedURLAsync(new GetPreSignedUrlRequest
            {
                BucketName = bucket, Key = R2Options.Key(key), Verb = HttpVerb.GET, Expires = expiresAt.UtcDateTime,
                Protocol = Protocol.HTTPS
            });
        }
        catch (Exception ex) when (ex is AmazonServiceException or AmazonClientException or HttpRequestException) { throw Unavailable(ex); }
    }

    public async Task<string> ReadHashAsync(string key, CancellationToken ct)
    {
        try
        {
            using var response = await s3.GetObjectAsync(new GetObjectRequest { BucketName = bucket, Key = R2Options.Key(key) }, ct);
            var hash = await System.Security.Cryptography.SHA256.HashDataAsync(response.ResponseStream, ct);
            return Convert.ToHexString(hash).ToLowerInvariant();
        }
        catch (Exception ex) when (ex is AmazonServiceException or AmazonClientException or HttpRequestException) { throw Unavailable(ex); }
    }
}
