using Amazon.S3;
using Amazon.S3.Model;

namespace NextStep.PosReports.Infrastructure.Storage;

public sealed class S3ObjectStorage(IAmazonS3 s3Client, string bucketName) : IObjectStorage
{
    public async Task PutTextAsync(string key, string content, string contentType, CancellationToken cancellationToken = default)
    {
        var request = new PutObjectRequest
        {
            BucketName = bucketName,
            Key = key,
            ContentBody = content,
            ContentType = contentType,
        };

        await s3Client.PutObjectAsync(request, cancellationToken);
    }

    public async Task<string> GetTextAsync(string key, CancellationToken cancellationToken = default)
    {
        using var response = await s3Client.GetObjectAsync(bucketName, key, cancellationToken);
        using var reader = new StreamReader(response.ResponseStream);

        return await reader.ReadToEndAsync(cancellationToken);
    }
}
