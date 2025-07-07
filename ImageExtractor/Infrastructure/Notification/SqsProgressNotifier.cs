using Amazon.SQS;
using Amazon.SQS.Model;
using ImageExtractor.Application.Interfaces;
using ImageExtractor.Domain;
using System.Text.Json;

namespace ImageExtractor.Infrastructure.Notification;

public class SqsProgressNotifier(IAmazonSQS sqsClient, string queueUrl) : IProgressNotifier
{
    public async Task NotifyProgressAsync(string videoId, int progress, int currentBlock, int totalBlocks)
    {
        var messageBody = JsonSerializer.Serialize(new
        {
            video_id = videoId,
            status = JobStatusEnum.Running,
            progress,
            current_block = currentBlock,
            total_blocks = totalBlocks,
            timestamp = DateTime.UtcNow
        });
        await sqsClient.SendMessageAsync(new SendMessageRequest
        {
            QueueUrl = queueUrl,
            MessageBody = messageBody
        });
    }
}