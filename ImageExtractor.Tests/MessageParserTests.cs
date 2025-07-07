using Amazon.Lambda.SQSEvents;
using ImageExtractor.Infrastructure.Messaging;
using System.Text.Json;

namespace ImageExtractor.Tests;

public class MessageParserTests
{
    private readonly MessageParser _parser = new MessageParser();

    [Fact]
    public void Parse_Throws_On_Empty_Body()
    {
        var msg = new SQSEvent.SQSMessage { Body = "" };
        Assert.Throws<InvalidDataException>(() => _parser.Parse(msg));
    }

    [Fact]
    public void Parse_Throws_On_Invalid_Json()
    {
        var msg = new SQSEvent.SQSMessage { Body = "not json" };
        Assert.Throws<InvalidDataException>(() => _parser.Parse(msg));
    }

    [Fact]
    public void Parse_Returns_Valid_Message()
    {
        var dto = new { video_id = "v1", bucket = "b", key = "k" };
        var body = JsonSerializer.Serialize(dto);
        var msg = new SQSEvent.SQSMessage { Body = body };

        var result = _parser.Parse(msg);

        Assert.Equal("v1", result.JobId);
        Assert.Equal("b", result.SourceBucket);
        Assert.Equal("k", result.SourceKey);
    }
}
