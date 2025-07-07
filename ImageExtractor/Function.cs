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

[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer))]

namespace ImageExtractor;

public class Function
{
    private readonly ServiceProvider _serviceProvider;
    private readonly IMessageParser _messageParser;
    private readonly IHub _sentryHub;

    public Function()
    {
        var sentryDsn = Environment.GetEnvironmentVariable("SENTRY_DSN")
            ?? throw new InvalidOperationException("Variável de ambiente 'SENTRY_DSN' não está configurada.");

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
            throw new InvalidOperationException("Falha ao inicializar o Sentry", ex);
        }

        var services = new ServiceCollection();
        ConfigureServices(services);
        _serviceProvider = services.BuildServiceProvider();

        _serviceProvider.GetRequiredService<IEnvironmentValidator>().Validate();
        _messageParser = _serviceProvider.GetRequiredService<IMessageParser>();
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<IConfigProvider, ConfigProvider>();
        services.AddSingleton(sp => sp.GetRequiredService<IConfigProvider>().LoadConfig());

        services.AddSingleton<IAmazonS3, AmazonS3Client>();
        services.AddSingleton<IAmazonSQS, AmazonSQSClient>();

        services.AddSingleton<IEnvironmentValidator, EnvironmentValidator>();
        services.AddSingleton<IVideoStorage, S3VideoStorage>();
        services.AddSingleton<ITransferUtility, TransferUtility>();
        services.AddSingleton<IZipService, ZipService>();
        services.AddSingleton<IVideoAnalyzer>(sp => new FfprobeVideoAnalyzer("/opt/bin/ffprobe"));
        services.AddSingleton<IFrameExtractor>(sp => new FfmpegFrameExtractor("/opt/bin/ffmpeg"));
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
    }

    public async Task FunctionHandler(SQSEvent sqsEvent, ILambdaContext context)
    {
        SentrySdk.ConfigureScope(scope =>
        {
            scope.SetTag("lambda_arn", context.InvokedFunctionArn);
            scope.SetTag("aws_request_id", context.AwsRequestId);
        });

        try
        {
            var workflow = _serviceProvider.GetRequiredService<IImageExtractionWorkflow>();

            var messages = sqsEvent.Records.Select(rec => _messageParser.Parse(rec));

            if (messages.Count() != 1)
            {
                throw new InvalidOperationException("Only one video can be processed at same time");
            }

            var appLogger = new LambdaContextLogger(context.Logger);

            await workflow.ExecuteAsync(messages, appLogger);
        }
        catch (Exception ex)
        {
            SentrySdk.CaptureException(ex);
            throw;
        }
        finally
        {
            await SentrySdk.FlushAsync(TimeSpan.FromSeconds(10));
        }
    }

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
}