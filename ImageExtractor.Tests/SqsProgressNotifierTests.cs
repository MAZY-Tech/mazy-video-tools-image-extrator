using Amazon.Lambda.Core;
using Amazon.SQS;
using Amazon.SQS.Model;
using ImageExtractor.Infrastructure.Adapters;
using ImageExtractor.Infrastructure.Notification;
using Moq;
using System.Text.Json;

namespace ImageExtractor.Tests;

public class SqsProgressNotifierTests
{
    [Fact]
    public async Task NotifyProgressAsync_Sends_Correct_Message()
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

        var notifier = new SqsProgressNotifier(sqs.Object, "url");
        await notifier.NotifyProgressAsync("vid", 42, 2, 5, appLogger);

        Assert.Equal("url", captured.QueueUrl);
        using var doc = JsonDocument.Parse(captured.MessageBody);
        var root = doc.RootElement;
        Assert.Equal("vid", root.GetProperty("video_id").GetString());
        Assert.Equal(42, root.GetProperty("progress").GetInt32());
        Assert.Equal(2, root.GetProperty("current_block").GetInt32());
        Assert.Equal(5, root.GetProperty("total_blocks").GetInt32());
    }
}
