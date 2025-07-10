using Amazon.Lambda.Core;
using Amazon.S3;
using Amazon.S3.Model;
using ImageExtractor.Infrastructure.Adapters;
using ImageExtractor.Infrastructure.Storage;
using Moq;
using System.Text;

namespace ImageExtractor.Tests;

public class S3VideoStorageTests
{
    private readonly Mock<IAmazonS3> _mockS3Client;
    private readonly S3VideoStorage _storage;

    public S3VideoStorageTests()
    {
        _mockS3Client = new Mock<IAmazonS3>();
        _storage = new S3VideoStorage(_mockS3Client.Object);
    }

    [Fact]
    public async Task DownloadVideoAsync_ShouldWriteS3StreamToFile_AndReturnPath()
    {
        var lambdaLogger = new Mock<ILambdaLogger>();
        var appLogger = new LambdaContextLogger(lambdaLogger.Object);
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
            var resultPath = await _storage.DownloadVideoAsync(bucketName, key, tempOutputPath, appLogger);

            Assert.Equal(tempOutputPath, resultPath);
            Assert.True(File.Exists(resultPath));

            var fileContent = await File.ReadAllTextAsync(resultPath);
            Assert.Equal(fakeVideoContent, fileContent);
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
    public async Task UploadFramesAsync_ShouldCallPutObjectForEachFrame_WithCorrectKey()
    {
        var lambdaLogger = new Mock<ILambdaLogger>();
        var appLogger = new LambdaContextLogger(lambdaLogger.Object);
        var bucketName = "output-bucket";
        var prefix = "job-123/frames";
        var framePaths = new[]
        {
        "/tmp/frame_001.jpg",
        "C:\\temp\\frames\\frame_002.jpg"
    };

        await _storage.UploadFramesAsync(bucketName, prefix, framePaths, appLogger);

        _mockS3Client.Verify(
            s => s.PutObjectAsync(
                It.IsAny<PutObjectRequest>(),
                It.IsAny<CancellationToken>()
            ),
            Times.Exactly(2)
        );

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
                    r.Key.EndsWith("frame_002.jpg") &&
                    r.Key.StartsWith("job-123/frames") &&
                    r.FilePath == framePaths[1]
                ),
                It.IsAny<CancellationToken>()
            ),
            Times.Once
        );
    }
}