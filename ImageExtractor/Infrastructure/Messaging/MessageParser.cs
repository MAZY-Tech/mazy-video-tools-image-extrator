using Amazon.Lambda.SQSEvents;
using ImageExtractor.Application.Interfaces;
using ImageExtractor.Domain;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ImageExtractor.Infrastructure.Messaging;

public class MessageParser : IMessageParser
{
    public ProcessingMessage Parse(SQSEvent.SQSMessage message)
    {
        if (string.IsNullOrWhiteSpace(message.Body))
        {
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
                throw new InvalidDataException("A mensagem não contém os campos obrigatórios (video_id, bucket, key).");
            }

            return new ProcessingMessage
            {
                JobId = rawMessage.VideoId,
                SourceBucket = rawMessage.Bucket,
                SourceKey = rawMessage.Key,
            };
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("JSON inválido no corpo da mensagem SQS.", ex);
        }
    }

    private class SqsMessageDto
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