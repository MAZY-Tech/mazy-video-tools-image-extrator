using Amazon.Lambda.Core;
using Amazon.Lambda.SQSEvents;
using Amazon.S3;
using Amazon.S3.Transfer;
using Amazon.SQS;
using ImageExtractor.Application.Interfaces;
using ImageExtractor.Application.Workflow;
using ImageExtractor.Infrastructure.Adapters;
using ImageExtractor.Infrastructure.Config;
using ImageExtractor.Infrastructure.Messaging;
using ImageExtractor.Infrastructure.Notification;
using ImageExtractor.Infrastructure.Repositories;
using ImageExtractor.Infrastructure.Storage;
using ImageExtractor.Infrastructure.VideoProcessing;
using Microsoft.Extensions.DependencyInjection;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text.Json;

[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer))]

namespace ImageExtractor;

public class Function
{
    private readonly ServiceProvider _serviceProvider;
    private readonly IMessageParser _messageParser;

    [EditorBrowsable(EditorBrowsableState.Never)]
    private readonly IHub? _sentryHub;

    /// <summary>
    /// Method for testing purposes only.
    /// </summary>
    /// <param name="serviceProvider"></param>
    /// <param name="sentryHub"></param>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public Function(ServiceProvider serviceProvider, IHub sentryHub)
    {
        _serviceProvider = serviceProvider;
        _sentryHub = sentryHub;

        _serviceProvider.GetRequiredService<IEnvironmentValidator>().Validate();
        _messageParser = _serviceProvider.GetRequiredService<IMessageParser>();
    }

    public Function()
    {
        Console.WriteLine("[LOG] Initializing Lambda function...");

        Console.WriteLine("[LOG] Checking 'SENTRY_DSN' environment variable...");
        var sentryDsn = Environment.GetEnvironmentVariable("SENTRY_DSN")
            ?? throw new InvalidOperationException("'SENTRY_DSN' environment variable is not configured.");

        Console.WriteLine("[LOG] Initializing Sentry with the provided DSN...");
        try
        {
            SentrySdk.Init(o =>
            {
                o.Dsn = sentryDsn;
                o.AttachStacktrace = true;
                o.TracesSampleRate = 1.0;
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine("[LOG] Error initializing Sentry: " + ex.Message);
            throw new InvalidOperationException("Failed to initialize Sentry", ex);
        }
        Console.WriteLine("[LOG] Sentry initialized successfully.");

        Console.WriteLine("[LOG] Starting ServiceProvider construction...");
        var services = new ServiceCollection();

        ConfigureServices(services);

        _serviceProvider = services.BuildServiceProvider();
        Console.WriteLine("[LOG] ServiceProvider built successfully.");

        _serviceProvider.GetRequiredService<IEnvironmentValidator>().Validate();
        Console.WriteLine("[LOG] Environment validated successfully.");

        _messageParser = _serviceProvider.GetRequiredService<IMessageParser>();

        Console.WriteLine("[LOG] Lambda function initialized successfully.");
    }

    private static void ConfigureServices(IServiceCollection services)
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
    }

    public async Task FunctionHandler(SQSEvent sqsEvent, ILambdaContext context)
    {
        var logger = new LambdaContextLogger(context.Logger);

        logger.Log("FunctionHandler started.");
        logger.Log($"Received {sqsEvent.Records.Count} records from SQS.");
        logger.Log($"SQS Event: {JsonSerializer.Serialize(sqsEvent)}");

        logger.Log($"Configuring Sentry, with lambda_arn={context.InvokedFunctionArn} and aws_request_id={context.AwsRequestId}");
        SentrySdk.ConfigureScope(scope =>
        {
            scope.SetTag("lambda_arn", context.InvokedFunctionArn);
            scope.SetTag("aws_request_id", context.AwsRequestId);
        });
        logger.Log("Sentry configuration completed.");

        try
        {
            logger.Log("Resolving workflow from ServiceProvider.");
            var workflow = _serviceProvider.GetRequiredService<IImageExtractionWorkflow>();

            var messages = sqsEvent.Records.Select(rec => _messageParser.Parse(rec, logger));

            if (messages.Count() != 1)
            {
                logger.Log($"ERROR: The function expected 1 message, but received {messages.Count()}.");
                throw new InvalidOperationException("Only one video can be processed at the same time");
            }
            logger.Log($"Message parsed successfully. JobId: {messages.First().JobId}");

            logger.Log("Executing workflow...");
            await workflow.ExecuteAsync(messages, logger);
            logger.Log("Workflow executed successfully.");
        }
        catch (Exception ex)
        {
            logger.Log($"Exception caught in FunctionHandler: {ex.Message} | StackTrace: {ex.StackTrace}");
            SentrySdk.CaptureException(ex);
            throw;
        }
        finally
        {
            logger.Log("Finalizing handler, flushing Sentry.");
            await SentrySdk.FlushAsync(TimeSpan.FromSeconds(10));
            logger.Log("Sentry flush completed.");
        }
    }
}