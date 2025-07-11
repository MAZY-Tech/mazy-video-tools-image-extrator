using Amazon.S3;
using Amazon.SQS;
using ImageExtractor.Application.Interfaces;
using ImageExtractor.Infrastructure.Config;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace ImageExtractor.Tests;

public class DependencyInjectionTests
{
    [Fact]
    public void AddImageExtractorServices_Should_Resolve_All_Dependencies_Successfully()
    {
        var services = new ServiceCollection();

        var dummyConfig = new ConfigProcessing
        {
            FramesBucket = "dummy-frames-bucket",
            ZipBucket = "dummy-zip-bucket",
            ProgressQueueUrl = "https://sqs.us-east-1.amazonaws.com/123456789012/dummy-queue",
            MongoDbHost = "localhost:27017",
            MongoDbUser = "testuser",
            MongoDbPassword = "testpassword",
            DatabaseName = "testdb",
            CollectionName = "testcollection",
            FrameExtension = "jpg",
            FrameRate = 1,
            BlockSize = 30
        };

        var mockConfigProvider = new Mock<IConfigProvider>();
        mockConfigProvider.Setup(p => p.LoadConfig()).Returns(dummyConfig);

        services.AddImageExtractorServices();

        ReplaceServiceWithMock<IAmazonS3>(services);
        ReplaceServiceWithMock<IAmazonSQS>(services);
        ReplaceServiceWithMock<IJobStateRepository>(services);

        ReplaceServiceWithInstance(services, mockConfigProvider.Object);

        var serviceProvider = services.BuildServiceProvider();

        var workflow = serviceProvider.GetService<IImageExtractionWorkflow>();
        Assert.NotNull(workflow);

        Assert.NotNull(serviceProvider.GetService<ICompletionNotifier>());
    }

    private static void ReplaceServiceWithMock<T>(ServiceCollection services) where T : class
    {
        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(T));
        if (descriptor != null)
        {
            services.Remove(descriptor);
        }
        services.AddSingleton(new Mock<T>().Object);
    }

    private static void ReplaceServiceWithInstance<T>(ServiceCollection services, T instance) where T : class
    {
        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(T));
        if (descriptor != null)
        {
            services.Remove(descriptor);
        }
        services.AddSingleton(instance);
    }
}