using Amazon.S3;
using Amazon.S3.Model;
using ImageExtractor.Application.Interfaces;

namespace ImageExtractor.Infrastructure.Storage;

public class S3VideoStorage(IAmazonS3 s3Client) : IVideoStorage
{
    public async Task<string> DownloadVideoAsync(string bucket, string key, string outputPath)
    {
        var request = new GetObjectRequest
        {
            BucketName = bucket,
            Key = key
        };

        using var response = await s3Client.GetObjectAsync(request);
        await using var responseStream = response.ResponseStream;
        await using var fileStream = File.Create(outputPath);
        await responseStream.CopyToAsync(fileStream);

        return outputPath;
    }

    public async Task UploadFramesAsync(string bucket, string prefix, string[] framePaths)
    {
        foreach (var path in framePaths)
        {
            var key = Path.Combine(prefix, Path.GetFileName(path)).Replace("\\", "/");

            var request = new PutObjectRequest
            {
                BucketName = bucket,
                Key = key,
                FilePath = path
            };

            await s3Client.PutObjectAsync(request);
        }
    }
}
