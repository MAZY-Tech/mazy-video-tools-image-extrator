using Amazon.S3;
using Amazon.S3.Transfer;
using Amazon.SQS;
using ImageExtractor.Application.Interfaces;
using ImageExtractor.Application.Workflow;
using ImageExtractor.Infrastructure.Messaging;
using ImageExtractor.Infrastructure.Notification;
using ImageExtractor.Infrastructure.Repositories;
using ImageExtractor.Infrastructure.Storage;
using ImageExtractor.Infrastructure.VideoProcessing;
using Microsoft.Extensions.DependencyInjection;
using System.Runtime.InteropServices;

namespace ImageExtractor.Infrastructure.Config;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddImageExtractorServices(this IServiceCollection services)
    {
        Console.WriteLine("[LOG] Starting ConfigureServices...");

        Console.WriteLine("[LOG] Determining architecture");
        var architecture = RuntimeInformation.ProcessArchitecture;
        string ffmpegPath;
        string ffprobePath;
        Console.WriteLine($"[LOG] Detected architecture: {architecture}");

        switch (architecture)
        {
            case Architecture.X64:
                Console.WriteLine("[LOG] Using binaries for x86_64.");
                ffmpegPath = "/opt/bin/x86_64/ffmpeg";
                ffprobePath = "/opt/bin/x86_64/ffprobe";
                break;

            default:
                throw new PlatformNotSupportedException($"Unsupported architecture: {architecture}");
        }

        services.AddSingleton<IConfigProvider, ConfigProvider>();
        services.AddSingleton(sp => sp.GetRequiredService<IConfigProvider>().LoadConfig());

        services.AddSingleton<IAmazonS3, AmazonS3Client>();
        services.AddSingleton<IAmazonSQS, AmazonSQSClient>();

        services.AddSingleton<IEnvironmentValidator, EnvironmentValidator>();
        services.AddSingleton<IVideoStorage, S3VideoStorage>();
        services.AddSingleton<ITransferUtility, TransferUtility>();
        services.AddSingleton<IZipService, ZipService>();
        services.AddSingleton<IVideoAnalyzer>(sp => new FfprobeVideoAnalyzer(ffprobePath));
        services.AddSingleton<IFrameExtractor>(sp => new FfmpegFrameExtractor(ffmpegPath));
        services.AddSingleton<IJobStateRepository, MongoJobStateRepository>();
        services.AddSingleton<IMessageParser, MessageParser>();

        services.AddSingleton<IProgressNotifier>(sp => new SqsProgressNotifier(
            sp.GetRequiredService<IAmazonSQS>(),
            sp.GetRequiredService<ConfigProcessing>().ProgressQueueUrl
        ));
        services.AddSingleton<ICompletionNotifier>(sp => new SqsCompletionNotifier(
            sp.GetRequiredService<IAmazonSQS>(),
            sp.GetRequiredService<ConfigProcessing>().ProgressQueueUrl
        ));

        services.AddTransient<IImageExtractionWorkflow, ImageExtractionWorkflow>();

        Console.WriteLine("[LOG] ConfigureServices completed.");

        return services;
    }
}