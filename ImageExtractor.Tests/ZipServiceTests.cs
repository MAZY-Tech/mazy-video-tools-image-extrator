using Amazon.Lambda.Core;
using Amazon.S3;
using Amazon.S3.Transfer;
using ImageExtractor.Infrastructure.Adapters;
using ImageExtractor.Infrastructure.Storage;
using Moq;
using System.IO.Compression;

namespace ImageExtractor.Tests;

public class ZipServiceTests
{
    [Fact]
    public async Task CreateZipAsync_ShouldCreateZipFile_FromSourceDirectory()
    {
        var lambdaLogger = new Mock<ILambdaLogger>();
        var appLogger = new LambdaContextLogger(lambdaLogger.Object);
        var sourceDirectory = Path.Combine(Path.GetTempPath(), "zip_test_source_" + Guid.NewGuid());
        var zipFilePath = Path.Combine(Path.GetTempPath(), "test.zip");
        Directory.CreateDirectory(sourceDirectory);

        await File.WriteAllTextAsync(Path.Combine(sourceDirectory, "sample.txt"), "hello world");

        var mockTransferUtility = new Mock<ITransferUtility>();
        var zipService = new ZipService(mockTransferUtility.Object);

        try
        {
            var resultPath = await zipService.CreateZipAsync(sourceDirectory, zipFilePath, appLogger);

            Assert.Equal(zipFilePath, resultPath);
            Assert.True(File.Exists(zipFilePath));

            using var zipArchive = ZipFile.OpenRead(zipFilePath);
            Assert.Single(zipArchive.Entries);
            Assert.Equal("sample.txt", zipArchive.Entries[0].Name);
        }
        finally
        {
            if (File.Exists(zipFilePath)) File.Delete(zipFilePath);
            if (Directory.Exists(sourceDirectory)) Directory.Delete(sourceDirectory, true);
        }
    }

    [Fact]
    public async Task UploadZipAsync_ShouldCallTransferUtility_WithCorrectParameters()
    {
        var lambdaLogger = new Mock<ILambdaLogger>();
        var appLogger = new LambdaContextLogger(lambdaLogger.Object);
        var mockTransferUtility = new Mock<ITransferUtility>();
        var zipService = new ZipService(mockTransferUtility.Object);

        var bucketName = "test-bucket";
        var objectKey = "archives/my-file.zip";
        var filePath = "/tmp/my-file.zip";

        await zipService.UploadZipAsync(bucketName, objectKey, filePath, appLogger);

        mockTransferUtility.Verify(
            t => t.UploadAsync(
                It.Is<string>(path => path == filePath),
                It.Is<string>(b => b == bucketName),
                It.Is<string>(k => k == objectKey),
                It.IsAny<CancellationToken>()
            ),
            Times.Once
        );
    }
}