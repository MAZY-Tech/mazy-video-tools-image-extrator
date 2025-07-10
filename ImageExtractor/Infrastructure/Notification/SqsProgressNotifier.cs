using Amazon.SQS;
using Amazon.SQS.Model;
using ImageExtractor.Application.Interfaces;
using ImageExtractor.Domain;
using System.Text.Json;

namespace ImageExtractor.Infrastructure.Notification;

public class SqsProgressNotifier(IAmazonSQS sqsClient, string queueUrl) : IProgressNotifier
{
    public async Task NotifyProgressAsync(string videoId, int progress, int currentBlock, int totalBlocks, IAppLogger logger)
    {
        logger.Log($"[SqsProgressNotifier] Attempting to send progress notification for JobId: {videoId}");
        logger.Log($"[SqsProgressNotifier] Target Queue URL: {queueUrl}");

        var messageBody = JsonSerializer.Serialize(new
        {
            video_id = videoId,
            status = JobStatus.Running.ToString().ToUpper(),
            progress,
            current_block = currentBlock,
            total_blocks = totalBlocks,
            timestamp = DateTime.UtcNow
        });

        logger.Log($"[SqsProgressNotifier] Message Body JSON: {messageBody}");

        try
        {
            await sqsClient.SendMessageAsync(new SendMessageRequest
            {
                QueueUrl = queueUrl,
                MessageBody = messageBody
            });

            logger.Log($"[SqsProgressNotifier] Progress notification sent successfully for JobId: {videoId}.");
        }
        catch (Exception ex)
        {
            logger.Log($"[SqsProgressNotifier] [ERROR] Failed to send message to SQS. JobId: {videoId}. Exception: {ex.Message}");
            throw;
        }
    }
}