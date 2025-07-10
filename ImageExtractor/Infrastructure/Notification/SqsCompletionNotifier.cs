using Amazon.SQS;
using Amazon.SQS.Model;
using ImageExtractor.Application.Interfaces;
using ImageExtractor.Domain;
using System.Text.Json;

namespace ImageExtractor.Infrastructure.Notification;

public class SqsCompletionNotifier(IAmazonSQS sqsClient, string queueUrl) : ICompletionNotifier
{
    public async Task NotifyCompletionAsync(string videoId, string zipBucket, string zipKey, IAppLogger logger)
    {
        logger.Log($"[SqsCompletionNotifier] Attempting to send completion notification for JobId: {videoId}");
        logger.Log($"[SqsCompletionNotifier] Target Queue URL: {queueUrl}");

        var messageBody = JsonSerializer.Serialize(new
        {
            video_id = videoId,
            status = JobStatus.Completed.ToString().ToUpper(),
            progress = 100,
            timestamp = DateTime.UtcNow,
            zip = new { bucket = zipBucket, key = zipKey }
        });

        logger.Log($"[SqsCompletionNotifier] Message Body JSON: {messageBody}");

        try
        {
            await sqsClient.SendMessageAsync(new SendMessageRequest
            {
                QueueUrl = queueUrl,
                MessageBody = messageBody
            });

            logger.Log($"[SqsCompletionNotifier] Completion notification sent successfully for JobId: {videoId}.");
        }
        catch (Exception ex)
        {
            logger.Log($"[SqsCompletionNotifier] [ERROR] Failed to send completion message to SQS. JobId: {videoId}. Exception: {ex.Message}");
            throw;
        }
    }
}