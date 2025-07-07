using Amazon.S3.Transfer;
using ImageExtractor.Application.Interfaces;
using System.IO.Compression;

namespace ImageExtractor.Infrastructure.Storage;

public class ZipService(ITransferUtility transferUtility) : IZipService
{
    public async Task<string> CreateZipAsync(string sourceDirectory, string zipPath)
    {
        await Task.CompletedTask;

        if (File.Exists(zipPath))
        {
            File.Delete(zipPath);
        }

        ZipFile.CreateFromDirectory(sourceDirectory, zipPath, CompressionLevel.Optimal, false);
        return zipPath;
    }

    public async Task UploadZipAsync(string bucket, string key, string zipPath)
    {
        await transferUtility.UploadAsync(zipPath, bucket, key);
    }
}
