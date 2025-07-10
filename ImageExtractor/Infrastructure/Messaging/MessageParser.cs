using Amazon.Lambda.SQSEvents;
using ImageExtractor.Application.Interfaces;
using ImageExtractor.Domain;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ImageExtractor.Infrastructure.Messaging;

public class MessageParser : IMessageParser
{
    public ProcessingMessage Parse(SQSEvent.SQSMessage message, IAppLogger logger)
    {
        logger.Log("[MessageParser] Starting to parse new SQS message.");

        if (string.IsNullOrWhiteSpace(message.Body))
        {
            logger.Log("[MessageParser] [ERROR] SQS message body is null or empty. Throwing InvalidDataException.");
            throw new InvalidDataException("O corpo da mensagem SQS está vazio.");
        }

        try
        {
            var rawMessage = JsonSerializer.Deserialize<SqsMessageDto>(message.Body);

            if (rawMessage == null ||
                string.IsNullOrWhiteSpace(rawMessage.VideoId) ||
                string.IsNullOrWhiteSpace(rawMessage.Bucket) ||
                string.IsNullOrWhiteSpace(rawMessage.Key))
            {
                logger.Log("[MessageParser] [ERROR] The message is missing required fields (video_id, bucket, key). Throwing InvalidDataException.");
                throw new InvalidDataException("A mensagem não contém os campos obrigatórios (video_id, bucket, key).");
            }

            var processingMessage = new ProcessingMessage
            {
                JobId = rawMessage.VideoId,
                SourceBucket = rawMessage.Bucket,
                SourceKey = rawMessage.Key,
            };

            logger.Log($"[MessageParser] Message parsed successfully. JobId: {processingMessage.JobId}");
            return processingMessage;
        }
        catch (JsonException ex)
        {
            logger.Log($"[MessageParser] [ERROR] Invalid JSON in SQS message body. Exception: {ex.Message}");
            throw new InvalidDataException("JSON inválido no corpo da mensagem SQS.", ex);
        }
    }

    private sealed class SqsMessageDto
    {
        [JsonPropertyName("video_id")]
        public string? VideoId { get; set; }

        [JsonPropertyName("bucket")]
        public string? Bucket { get; set; }

        [JsonPropertyName("key")]
        public string? Key { get; set; }

        [JsonExtensionData]
        public Dictionary<string, JsonElement>? ExtensionData { get; set; }
    }
}