using Amazon.S3;
using Amazon.S3.Model;
using ImageExtractor.Application.Interfaces;

namespace ImageExtractor.Infrastructure.Storage;

public class S3VideoStorage(IAmazonS3 s3Client) : IVideoStorage
{
    /// <summary>
    /// Downloads a video file from S3 to a local path.
    /// </summary>
    public async Task<string> DownloadVideoAsync(string bucket, string key, string outputPath, IAppLogger logger)
    {
        logger.Log($"[S3Storage] Starting download from s3://{bucket}/{key} to local path '{outputPath}'");
        try
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

            logger.Log($"[S3Storage] Download completed successfully. Local file size: {fileStream.Length / 1024.0:F2} KB");
            return outputPath;
        }
        catch (Exception ex)
        {
            logger.Log($"[S3Storage] [ERROR] Failed to download video from S3. Exception: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Uploads multiple frame files to S3 in parallel.
    /// </summary>
    public async Task UploadFramesAsync(string bucket, string prefix, string[] framePaths, IAppLogger logger)
    {
        if (framePaths.Length == 0)
        {
            logger.Log("[S3Storage] No frames to upload, skipping.");
            return;
        }

        logger.Log($"[S3Storage] Starting parallel upload of {framePaths.Length} frames to s3://{bucket}/{prefix}");

        // We create a list of upload tasks without awaiting them individually.
        var uploadTasks = framePaths.Select(path =>
        {
            var key = Path.Combine(prefix, Path.GetFileName(path)).Replace("\\", "/");

            var request = new PutObjectRequest
            {
                BucketName = bucket,
                Key = key,
                FilePath = path
            };

            // Return the task, don't await it here.
            return s3Client.PutObjectAsync(request);
        });

        try
        {
            // Task.WhenAll runs all the upload tasks concurrently and waits for them all to finish.
            await Task.WhenAll(uploadTasks);
            logger.Log($"[S3Storage] Successfully completed parallel upload of {framePaths.Length} frames.");
        }
        catch (Exception ex)
        {
            // If any of the uploads fail, Task.WhenAll will throw the first exception it encounters.
            logger.Log($"[S3Storage] [ERROR] An error occurred during the parallel upload of frames. Exception: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Downloads all frames from the specified S3 bucket and prefix to the local destination directory.
    /// </summary>
    /// <remarks>This method retrieves all objects from the specified S3 bucket and prefix, and downloads them
    /// in parallel to the specified local directory. It logs the progress and any errors encountered during the
    /// process. If no frames are found, it logs a message and exits without downloading.</remarks>
    /// <param name="bucket">The name of the S3 bucket from which to download frames.</param>
    /// <param name="prefix">The prefix used to filter objects in the S3 bucket.</param>
    /// <param name="destinationDir">The local directory where the downloaded frames will be saved. The directory will be created if it does not
    /// exist.</param>
    /// <param name="logger">An instance of <see cref="IAppLogger"/> used to log the progress and any errors during the download process.</param>
    /// <returns></returns>
    public async Task DownloadAllFramesAsync(string bucket, string prefix, string destinationDir, IAppLogger logger)
    {
        logger.Log($"[S3Storage] Starting download of all frames from s3://{bucket}/{prefix} to '{destinationDir}'");
        Directory.CreateDirectory(destinationDir);

        try
        {
            var allKeys = new List<string>();
            var listRequest = new ListObjectsV2Request
            {
                BucketName = bucket,
                Prefix = prefix
            };

            ListObjectsV2Response listResponse;
            do
            {
                listResponse = await s3Client.ListObjectsV2Async(listRequest);
                allKeys.AddRange(listResponse.S3Objects.Where(o => o.Size > 0).Select(o => o.Key));
                listRequest.ContinuationToken = listResponse.NextContinuationToken;
            }
            while (listResponse.IsTruncated == true);

            if (allKeys.Count == 0)
            {
                logger.Log("[S3Storage] No frames found at the specified prefix. Nothing to download.");
                return;
            }

            logger.Log($"[S3Storage] Found {allKeys.Count} frames. Starting parallel download.");

            var maxParallelism = 50;
            using var semaphore = new SemaphoreSlim(maxParallelism);

            var downloadTasks = allKeys.Select(async key =>
            {
                await semaphore.WaitAsync();
                try
                {
                    var localPath = Path.Combine(destinationDir, Path.GetFileName(key));
                    var getRequest = new GetObjectRequest
                    {
                        BucketName = bucket,
                        Key = key
                    };

                    using var response = await s3Client.GetObjectAsync(getRequest);
                    await using var responseStream = response.ResponseStream;
                    await using var fileStream = File.Create(localPath);
                    await responseStream.CopyToAsync(fileStream);
                }
                finally
                {
                    semaphore.Release();
                }
            });

            await Task.WhenAll(downloadTasks);
            logger.Log($"[S3Storage] Successfully downloaded {allKeys.Count} frames.");
        }
        catch (Exception ex)
        {
            logger.Log($"[S3Storage] [ERROR] Failed to download frames for zipping. Exception: {ex.Message}");
            throw;
        }
    }
}