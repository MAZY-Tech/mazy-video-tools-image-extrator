using Amazon.S3.Transfer;
using ImageExtractor.Application.Interfaces;
using System.IO.Compression;

namespace ImageExtractor.Infrastructure.Storage;

public class ZipService(ITransferUtility transferUtility) : IZipService
{
    /// <summary>
    /// Creates a zip file from a source directory asynchronously.
    /// </summary>
    public async Task<string> CreateZipAsync(string sourceDirectory, string zipPath, IAppLogger logger)
    {
        logger.Log($"[ZipService] Starting zip file creation from directory '{sourceDirectory}' to '{zipPath}'.");

        try
        {
            // Running the blocking I/O operation on a background thread to make it truly async.
            await Task.Run(() =>
            {
                if (File.Exists(zipPath))
                {
                    logger.Log($"[ZipService] Found an existing zip file. Deleting it first.");
                    File.Delete(zipPath);
                }

                var fileCount = Directory.GetFiles(sourceDirectory, "*", SearchOption.AllDirectories).Length;
                logger.Log($"[ZipService] Zipping {fileCount} files...");

                ZipFile.CreateFromDirectory(sourceDirectory, zipPath, CompressionLevel.Optimal, false);
            });

            var fileInfo = new FileInfo(zipPath);
            logger.Log($"[ZipService] Zip file created successfully. Path: {zipPath}, Size: {fileInfo.Length / 1024.0:F2} KB");
            return zipPath;
        }
        catch (Exception ex)
        {
            logger.Log($"[ZipService] [ERROR] Failed to create zip file. Exception: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Uploads a file to an S3 bucket.
    /// </summary>
    public async Task UploadZipAsync(string bucket, string key, string zipPath, IAppLogger logger)
    {
        logger.Log($"[ZipService] Starting upload of '{zipPath}' to s3://{bucket}/{key}");
        try
        {
            await transferUtility.UploadAsync(zipPath, bucket, key);
            logger.Log($"[ZipService] Upload to s3://{bucket}/{key} completed successfully.");
        }
        catch (Exception ex)
        {
            logger.Log($"[ZipService] [ERROR] Failed to upload zip file to S3. Exception: {ex.Message}");
            throw;
        }
    }
}