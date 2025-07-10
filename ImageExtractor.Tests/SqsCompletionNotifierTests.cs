using Amazon.Lambda.Core;
using Amazon.SQS;
using Amazon.SQS.Model;
using ImageExtractor.Infrastructure.Adapters;
using ImageExtractor.Infrastructure.Notification;
using Moq;
using System.Text.Json;

namespace ImageExtractor.Tests;

public class SqsCompletionNotifierTests
{
    [Fact]
    public async Task NotifyCompletionAsync_Sends_Correct_Message()
    {
        var lambdaLogger = new Mock<ILambdaLogger>();
        var appLogger = new LambdaContextLogger(lambdaLogger.Object);
        var sqs = new Mock<IAmazonSQS>();
        SendMessageRequest captured = null!;
        sqs.Setup(x => x.SendMessageAsync(
                It.IsAny<SendMessageRequest>(),
                It.IsAny<CancellationToken>()))
           .Callback<SendMessageRequest, CancellationToken>((r, _) => captured = r)
           .ReturnsAsync(new SendMessageResponse());

        var notifier = new SqsCompletionNotifier(sqs.Object, "url");
        await notifier.NotifyCompletionAsync("vid", "zb", "zk", appLogger);

        Assert.Equal("url", captured.QueueUrl);

        using var doc = JsonDocument.Parse(captured.MessageBody);
        var root = doc.RootElement;
        Assert.Equal("vid", root.GetProperty("video_id").GetString());
        Assert.Equal("COMPLETED", root.GetProperty("status").GetString());
        Assert.Equal(100, root.GetProperty("progress").GetInt32());
        Assert.Equal("zb", root.GetProperty("zip").GetProperty("bucket").GetString());
        Assert.Equal("zk", root.GetProperty("zip").GetProperty("key").GetString());
    }
}
