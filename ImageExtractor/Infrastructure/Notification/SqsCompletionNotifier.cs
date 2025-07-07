using Amazon.SQS;
using Amazon.SQS.Model;
using ImageExtractor.Application.Interfaces;
using ImageExtractor.Domain;
using System.Text.Json;

namespace ImageExtractor.Infrastructure.Notification;

public class SqsCompletionNotifier(IAmazonSQS sqsClient, string queueUrl) : ICompletionNotifier
{
    public async Task NotifyCompletionAsync(string videoId, string zipBucket, string zipKey)
    {
        var messageBody = JsonSerializer.Serialize(new
        {
            video_id = videoId,
            status = JobStatusEnum.Completed.ToString(),
            progress = 100,
            timestamp = DateTime.UtcNow,
            zip = new { bucket = zipBucket, key = zipKey }
        });
        await sqsClient.SendMessageAsync(new SendMessageRequest
        {
            QueueUrl = queueUrl,
            MessageBody = messageBody
        });
    }
}