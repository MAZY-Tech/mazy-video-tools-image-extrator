using ImageExtractor.Application.Interfaces;

namespace ImageExtractor.Infrastructure.Config;

public class ConfigProvider : IConfigProvider
{
    public ConfigProcessing LoadConfig()
    {
        static string Get(string name, bool required = true, string? fallback = null)
        {
            var value = Environment.GetEnvironmentVariable(name);
            if (!string.IsNullOrWhiteSpace(value)) return value;
            if (fallback is not null) return fallback;
            if (required) throw new InvalidOperationException($"Variável de ambiente obrigatória não definida: {name}");
            return string.Empty;
        }

        return new ConfigProcessing
        {
            FramesBucket = Get("FRAMES_BUCKET_NAME"),
            ZipBucket = Get("ZIP_BUCKET_NAME"),
            ProgressQueueUrl = Get("PROGRESS_QUEUE_URL"),
            MongoDbHost = Get("MONGO_DB_HOST"),
            MongoDbUser = Get("MONGO_DB_USER"),
            MongoDbPassword = Get("MONGO_DB_PASSWORD"),
            DatabaseName = Get("DATABASE_NAME"),
            CollectionName = Get("COLLECTION_NAME"),
            TempFolder = Path.GetTempPath(),
            FrameExtension = Get("FRAME_EXTENSION", required: false, fallback: "jpg"),
            FrameRate = int.Parse(Get("FRAME_RATE", required: false, fallback: "1")),
            BlockSize = int.Parse(Get("BLOCK_SIZE", required: false, fallback: "30"))
        };
    }
}
