using Amazon.Lambda.Core;
using Amazon.Lambda.SQSEvents;
using ImageExtractor.Infrastructure.Adapters;
using ImageExtractor.Infrastructure.Messaging;
using Moq;
using System.Text.Json;

namespace ImageExtractor.Tests;

public class MessageParserTests
{
    private readonly MessageParser _parser = new MessageParser();

    [Fact]
    public void Parse_Throws_On_Empty_Body()
    {
        var lambdaLogger = new Mock<ILambdaLogger>();
        var appLogger = new LambdaContextLogger(lambdaLogger.Object);
        var msg = new SQSEvent.SQSMessage { Body = "" };
        Assert.Throws<InvalidDataException>(() => _parser.Parse(msg, appLogger));
    }

    [Fact]
    public void Parse_Throws_On_Invalid_Json()
    {
        var lambdaLogger = new Mock<ILambdaLogger>();
        var appLogger = new LambdaContextLogger(lambdaLogger.Object);
        var msg = new SQSEvent.SQSMessage { Body = "not json" };
        Assert.Throws<InvalidDataException>(() => _parser.Parse(msg, appLogger));
    }

    [Fact]
    public void Parse_Returns_Valid_Message()
    {
        var lambdaLogger = new Mock<ILambdaLogger>();
        var appLogger = new LambdaContextLogger(lambdaLogger.Object);
        var dto = new { video_id = "v1", bucket = "b", key = "k" };
        var body = JsonSerializer.Serialize(dto);
        var msg = new SQSEvent.SQSMessage { Body = body };

        var result = _parser.Parse(msg, appLogger);

        Assert.Equal("v1", result.JobId);
        Assert.Equal("b", result.SourceBucket);
        Assert.Equal("k", result.SourceKey);
    }
}
