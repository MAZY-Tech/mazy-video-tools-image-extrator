using Amazon.S3;
using Amazon.S3.Model;
using ImageExtractor.Application.Interfaces;
using ImageExtractor.Infrastructure.Storage;
using Moq;
using System.Text;

namespace ImageExtractor.Tests;

public class S3VideoStorageTests
{
    private readonly Mock<IAmazonS3> _mockS3Client;
    private readonly Mock<IAppLogger> _mockLogger;
    private readonly S3VideoStorage _storage;

    public S3VideoStorageTests()
    {
        _mockS3Client = new Mock<IAmazonS3>();
        _mockLogger = new Mock<IAppLogger>();
        _storage = new S3VideoStorage(_mockS3Client.Object);
    }

    [Fact]
    public async Task DownloadVideoAsync_ShouldWriteS3StreamToFile_AndReturnPath()
    {
        var bucketName = "test-bucket";
        var key = "videos/my-video.mp4";
        var tempOutputPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString() + ".mp4");
        var fakeVideoContent = "fake-binary-video-data";
        var responseStream = new MemoryStream(Encoding.UTF8.GetBytes(fakeVideoContent));

        var mockResponse = new GetObjectResponse
        {
            ResponseStream = responseStream
        };

        _mockS3Client
            .Setup(s => s.GetObjectAsync(It.Is<GetObjectRequest>(r => r.BucketName == bucketName && r.Key == key), It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockResponse);

        try
        {
            var resultPath = await _storage.DownloadVideoAsync(bucketName, key, tempOutputPath, _mockLogger.Object);

            Assert.Equal(tempOutputPath, resultPath);
            Assert.True(File.Exists(resultPath));

            var fileContent = await File.ReadAllTextAsync(resultPath);
            Assert.Equal(fakeVideoContent, fileContent);

            _mockLogger.Verify(l => l.Log(It.Is<string>(s => s.StartsWith("[S3Storage] Download completed successfully"))), Times.Once);
        }
        finally
        {
            if (File.Exists(tempOutputPath))
            {
                File.Delete(tempOutputPath);
            }
        }
    }

    [Fact]
    public async Task UploadFramesAsync_ShouldCallPutObjectForEachFrameInParallel()
    {
        var bucketName = "output-bucket";
        var prefix = "job-123/frames";
        var framePaths = new[]
        {
            Path.Combine(Path.GetTempPath(), "frame_001.jpg"),
            Path.Combine(Path.GetTempPath(), "frame_002.jpg")
        };

        foreach (var path in framePaths)
        {
            await File.WriteAllTextAsync(path, "fake-image-data");
        }

        await _storage.UploadFramesAsync(bucketName, prefix, framePaths, _mockLogger.Object);

        _mockS3Client.Verify(s => s.PutObjectAsync(It.IsAny<PutObjectRequest>(), It.IsAny<CancellationToken>()), Times.Exactly(2));

        _mockS3Client.Verify(
            s => s.PutObjectAsync(
                It.Is<PutObjectRequest>(r =>
                    r.BucketName == bucketName &&
                    r.Key == "job-123/frames/frame_001.jpg" &&
                    r.FilePath == framePaths[0]
                ),
                It.IsAny<CancellationToken>()
            ),
            Times.Once
        );

        _mockS3Client.Verify(
            s => s.PutObjectAsync(
                It.Is<PutObjectRequest>(r =>
                    r.BucketName == bucketName &&
                    r.Key == "job-123/frames/frame_002.jpg" &&
                    r.FilePath == framePaths[1]
                ),
                It.IsAny<CancellationToken>()
            ),
            Times.Once
        );

        _mockLogger.Verify(l => l.Log(It.Is<string>(s => s.Contains("Successfully completed parallel upload"))), Times.Once);

        foreach (var path in framePaths)
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Fact]
    public async Task UploadFramesAsync_ShouldDoNothing_WhenFramePathsIsEmpty()
    {
        var bucketName = "output-bucket";
        var prefix = "job-123/frames";
        var framePaths = Array.Empty<string>();

        await _storage.UploadFramesAsync(bucketName, prefix, framePaths, _mockLogger.Object);

        _mockS3Client.Verify(
            s => s.PutObjectAsync(It.IsAny<PutObjectRequest>(), It.IsAny<CancellationToken>()),
            Times.Never
        );
        _mockLogger.Verify(l => l.Log("[S3Storage] No frames to upload, skipping."), Times.Once);
    }

    [Fact]
    public async Task DownloadAllFramesAsync_ShouldDownloadAllListedObjects()
    {
        var bucket = "test-bucket";
        var prefix = "frames/";
        var destinationDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(destinationDir);

        var s3Objects = new List<S3Object>
        {
            new() { Key = "frames/frame1.jpg", Size = 10 },
            new() { Key = "frames/frame2.jpg", Size = 10 },
            new() { Key = "frames/subdirectory/", Size = 0 }
        };

        var firstListResponse = new ListObjectsV2Response
        {
            S3Objects = [s3Objects[0]],
            IsTruncated = true,
            NextContinuationToken = "token123"
        };

        var secondListResponse = new ListObjectsV2Response
        {
            S3Objects = [s3Objects[1], s3Objects[2]],
            IsTruncated = false
        };

        _mockS3Client.SetupSequence(c => c.ListObjectsV2Async(It.IsAny<ListObjectsV2Request>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(firstListResponse)
            .ReturnsAsync(secondListResponse);

        foreach (var s3Object in s3Objects.Where(o => o.Size > 0))
        {
            var fakeContent = $"content-for-{s3Object.Key}";
            var responseStream = new MemoryStream(Encoding.UTF8.GetBytes(fakeContent));
            var getObjectResponse = new GetObjectResponse { ResponseStream = responseStream };
            _mockS3Client.Setup(c => c.GetObjectAsync(It.Is<GetObjectRequest>(r => r.BucketName == bucket && r.Key == s3Object.Key), It.IsAny<CancellationToken>()))
                .ReturnsAsync(getObjectResponse);
        }

        try
        {
            await _storage.DownloadAllFramesAsync(bucket, prefix, destinationDir, _mockLogger.Object);

            _mockS3Client.Verify(c => c.ListObjectsV2Async(It.IsAny<ListObjectsV2Request>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
            _mockS3Client.Verify(c => c.GetObjectAsync(It.IsAny<GetObjectRequest>(), It.IsAny<CancellationToken>()), Times.Exactly(2));

            var expectedFile1 = Path.Combine(destinationDir, "frame1.jpg");
            var expectedFile2 = Path.Combine(destinationDir, "frame2.jpg");
            Assert.True(File.Exists(expectedFile1));
            Assert.True(File.Exists(expectedFile2));
            Assert.Equal("content-for-frames/frame1.jpg", await File.ReadAllTextAsync(expectedFile1));
            Assert.Equal("content-for-frames/frame2.jpg", await File.ReadAllTextAsync(expectedFile2));

            _mockLogger.Verify(l => l.Log(It.Is<string>(s => s.Contains("Successfully downloaded 2 frames"))), Times.Once);
        }
        finally
        {
            if (Directory.Exists(destinationDir))
            {
                Directory.Delete(destinationDir, true);
            }
        }
    }
}