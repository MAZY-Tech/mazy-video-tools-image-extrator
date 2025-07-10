using ImageExtractor.Application.Interfaces;

namespace ImageExtractor.Infrastructure.Config;

public class ConfigProvider : IConfigProvider
{
    private static readonly HashSet<string> _sensitiveKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "MONGO_DB_PASSWORD",
        "MONGO_DB_USER"
    };

    public ConfigProcessing LoadConfig()
    {
        Console.WriteLine("[LOG] Loading application configuration from environment variables...");

        var config = new ConfigProcessing
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

        Console.WriteLine("[LOG] Configuration loading completed.");
        return config;
    }

    /// <summary>
    /// Gets an environment variable, logs it securely, and handles required/fallback logic.
    /// </summary>
    private static string Get(string name, bool required = true, string? fallback = null)
    {
        var value = Environment.GetEnvironmentVariable(name);

        var logValue = _sensitiveKeys.Contains(name) ? "*****" : (value ?? "null");
        Console.WriteLine($"[LOG]   - Loading config: '{name}' = '{logValue}'");

        if (!string.IsNullOrWhiteSpace(value)) return value;

        if (fallback is not null)
        {
            Console.WriteLine($"[LOG]   - '{name}' not found. Using fallback: '{fallback}'");
            return fallback;
        }

        if (required)
        {
            throw new InvalidOperationException($"Required environment variable not set: {name}");
        }

        return string.Empty;
    }
}