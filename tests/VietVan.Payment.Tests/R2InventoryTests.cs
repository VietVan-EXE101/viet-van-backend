using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Options;
using VietVan.API.Services.Storage;
using Xunit;

namespace VietVan.Payment.Tests;

public class R2InventoryTests
{
    private sealed class InventoryClient(bool fail = false) : AmazonS3Client(new AnonymousAWSCredentials(),
        new AmazonS3Config { ServiceURL = "https://inventory.invalid", AuthenticationRegion = "auto" })
    {
        public readonly List<(string Bucket, string? Token)> Requests = [];
        public override Task<ListObjectsV2Response> ListObjectsV2Async(ListObjectsV2Request request, CancellationToken cancellationToken = default)
        {
            Requests.Add((request.BucketName, request.ContinuationToken));
            if (fail) throw new AmazonS3Exception("private-provider-message-secret-sentinel")
                { StatusCode = System.Net.HttpStatusCode.Forbidden, ErrorCode = "AccessDenied" };
            return Task.FromResult(request.ContinuationToken is null
                ? new ListObjectsV2Response { IsTruncated = true, NextContinuationToken = "page2", S3Objects = [new() { Key = "pro/assets/one.gif", Size = 12 }] }
                : new ListObjectsV2Response { IsTruncated = false, S3Objects = [new() { Key = "free/videos/two.mp4", Size = 16 }] });
        }
    }

    [Fact]
    public async Task ListsAllPagesWithConfiguredBucketWithoutTouchingObjects()
    {
        using var sdk = new InventoryClient();
        var options = R2StorageTests.Configuration();
        var entries = await new R2ObjectClient(sdk, Options.Create(options)).ListAsync(default);
        Assert.Equal(2, entries.Count); Assert.Equal(12, entries[0].Size); Assert.Equal(16, entries[1].Size);
        Assert.Equal(2, sdk.Requests.Count); Assert.Null(sdk.Requests[0].Token); Assert.Equal("page2", sdk.Requests[1].Token);
        Assert.All(sdk.Requests, request => Assert.Equal(options.BucketName, request.Bucket));
    }

    [Fact]
    public async Task InventoryErrorDiscardsProviderMessageAndKeepsOnlySafeCode()
    {
        using var sdk = new InventoryClient(fail: true);
        var error = await Assert.ThrowsAsync<MediaStorageUnavailableException>(() =>
            new R2ObjectClient(sdk, Options.Create(R2StorageTests.Configuration())).ListAsync(default));
        Assert.Equal("ACCESS_DENIED", error.FailureCode); Assert.Equal(403, error.HttpStatus);
        Assert.Null(error.InnerException); Assert.DoesNotContain("secret-sentinel", error.ToString());
    }
}
