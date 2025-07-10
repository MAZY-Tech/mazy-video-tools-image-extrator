using Amazon.Lambda.Core;
using Amazon.Lambda.SQSEvents;
using Amazon.Lambda.TestUtilities;
using ImageExtractor.Application.Interfaces;
using ImageExtractor.Domain;
using ImageExtractor.Infrastructure.Adapters;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace ImageExtractor.Tests;

public class FunctionTests : IDisposable
{
    private readonly Mock<IImageExtractionWorkflow> _mockWorkflow;
    private readonly Mock<IMessageParser> _mockMessageParser;
    private readonly Mock<IEnvironmentValidator> _mockEnvValidator;
    private readonly Mock<IHub> _mockSentryHub;
    private readonly ServiceProvider _serviceProvider;

    public FunctionTests()
    {
        _mockWorkflow = new Mock<IImageExtractionWorkflow>();

        _mockMessageParser = new Mock<IMessageParser>();
        _mockEnvValidator = new Mock<IEnvironmentValidator>();
        _mockSentryHub = new Mock<IHub>();

        _mockSentryHub.Setup(h => h.ConfigureScope(It.IsAny<Action<Scope>>()));

        var services = new ServiceCollection();

        services.AddSingleton<IImageExtractionWorkflow>(_mockWorkflow.Object);
        services.AddSingleton<IMessageParser>(_mockMessageParser.Object);
        services.AddSingleton<IEnvironmentValidator>(_mockEnvValidator.Object);
        _serviceProvider = services.BuildServiceProvider();
    }

    public void Dispose() => _serviceProvider.Dispose();

    [Fact]
    public void Constructor_Throws_When_SentryDsn_Not_Set()
    {
        Environment.SetEnvironmentVariable("SENTRY_DSN", null);
        var ex = Assert.Throws<InvalidOperationException>(() => new Function());
        Assert.Contains("SENTRY_DSN", ex.Message);
    }

    [Fact]
    public async Task FunctionHandler_WithSingleMessage_ShouldExecuteWorkflow()
    {
        var lambdaLogger = new Mock<ILambdaLogger>();
        var appLogger = new LambdaContextLogger(lambdaLogger.Object);
        var sqsEvent = new SQSEvent { Records = new List<SQSEvent.SQSMessage> { new() } };
        var context = new TestLambdaContext();
        _mockMessageParser.Setup(p => p.Parse(It.IsAny<SQSEvent.SQSMessage>(), It.IsAny<IAppLogger>())).Returns(new ProcessingMessage());

        var function = new Function(_serviceProvider, _mockSentryHub.Object);

        await function.FunctionHandler(sqsEvent, context);

        _mockWorkflow.Verify(w => w.ExecuteAsync(It.IsAny<IEnumerable<ProcessingMessage>>(), It.IsAny<IAppLogger>()), Times.Once);
    }

    [Fact]
    public async Task FunctionHandler_WithMultipleMessages_ShouldThrowAndCaptureException()
    {
        var lambdaLogger = new Mock<ILambdaLogger>();
        var appLogger = new LambdaContextLogger(lambdaLogger.Object);
        var sqsEvent = new SQSEvent { Records = new List<SQSEvent.SQSMessage> { new(), new() } };
        var context = new TestLambdaContext();
        _mockMessageParser.Setup(p => p.Parse(It.IsAny<SQSEvent.SQSMessage>(), It.IsAny<IAppLogger>())).Returns(new ProcessingMessage());

        var function = new Function(_serviceProvider, _mockSentryHub.Object);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => function.FunctionHandler(sqsEvent, context));

        Assert.Equal("Only one video can be processed at the same time", ex.Message);

        _mockWorkflow.Verify(w => w.ExecuteAsync(It.IsAny<IEnumerable<ProcessingMessage>>(), It.IsAny<IAppLogger>()), Times.Never);
    }
}