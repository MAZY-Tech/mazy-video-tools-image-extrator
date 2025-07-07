using Amazon.Lambda.Core;
using ImageExtractor.Infrastructure.Adapters;
using Moq;

namespace ImageExtractor.Tests;

public class LambdaContextLoggerTests
{
    [Fact]
    public void Log_Calls_LambdaLogger_LogLine()
    {
        var lambdaLogger = new Mock<ILambdaLogger>();
        var appLogger = new LambdaContextLogger(lambdaLogger.Object);

        appLogger.Log("hello");

        lambdaLogger.Verify(x => x.LogLine("hello"), Times.Once);
    }
}
