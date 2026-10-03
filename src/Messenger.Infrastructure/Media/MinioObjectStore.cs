using Messenger.Application.Media;
using Minio;
using Minio.DataModel.Args;

namespace Messenger.Infrastructure.Media;

public sealed class MinioOptions
{
    public string Endpoint { get; set; } = "http://localhost:9000";
    public string AccessKey { get; set; } = "messenger";
    public string SecretKey { get; set; } = "messenger-local-only";
    public string Bucket { get; set; } = "messenger-private";
}

public sealed class MinioObjectStore : IObjectStore
{
    private readonly IMinioClient _client;
    private readonly string _bucket;
    private readonly SemaphoreSlim _bucketLock = new(1, 1);
    private bool _bucketReady;

    public MinioObjectStore(MinioOptions options)
    {
        var endpoint = new Uri(options.Endpoint);
        _client = new MinioClient()
            .WithEndpoint(endpoint.Host, endpoint.Port)
            .WithCredentials(options.AccessKey, options.SecretKey)
            .WithSSL(endpoint.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase))
            .Build();
        _bucket = options.Bucket;
    }

    public async Task<string> CreateUploadUrlAsync(string key, string contentType, TimeSpan lifetime, CancellationToken ct)
    {
        await EnsureBucketAsync(ct);
        return await _client.PresignedPutObjectAsync(new PresignedPutObjectArgs()
            .WithBucket(_bucket).WithObject(key).WithExpiry((int)lifetime.TotalSeconds));
    }

    public async Task<ObjectMetadata> GetMetadataAsync(string key, CancellationToken ct)
    {
        await EnsureBucketAsync(ct);
        var value = await _client.StatObjectAsync(new StatObjectArgs().WithBucket(_bucket).WithObject(key), ct);
        return new ObjectMetadata(value.Size, value.ContentType);
    }

    public async Task<Stream> OpenReadAsync(string key, CancellationToken ct)
    {
        await EnsureBucketAsync(ct);
        var output = new MemoryStream();
        await _client.GetObjectAsync(new GetObjectArgs().WithBucket(_bucket).WithObject(key)
            .WithCallbackStream(stream => stream.CopyTo(output)), ct);
        output.Position = 0;
        return output;
    }

    public async Task<string> CreateDownloadUrlAsync(string key, TimeSpan lifetime, CancellationToken ct)
    {
        await EnsureBucketAsync(ct);
        return await _client.PresignedGetObjectAsync(new PresignedGetObjectArgs()
            .WithBucket(_bucket).WithObject(key).WithExpiry((int)lifetime.TotalSeconds));
    }

    public async Task DeleteAsync(string key, CancellationToken ct)
    {
        await EnsureBucketAsync(ct);
        await _client.RemoveObjectAsync(new RemoveObjectArgs().WithBucket(_bucket).WithObject(key), ct);
    }

    private async Task EnsureBucketAsync(CancellationToken ct)
    {
        if (_bucketReady) return;
        await _bucketLock.WaitAsync(ct);
        try
        {
            if (_bucketReady) return;
            if (!await _client.BucketExistsAsync(new BucketExistsArgs().WithBucket(_bucket), ct))
            {
                await _client.MakeBucketAsync(new MakeBucketArgs().WithBucket(_bucket), ct);
            }
            _bucketReady = true;
        }
        finally
        {
            _bucketLock.Release();
        }
    }
}
