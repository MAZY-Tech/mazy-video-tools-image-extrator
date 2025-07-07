using ImageExtractor.Infrastructure.Config;

namespace ImageExtractor.Tests;

public class ConfigProviderTests
{
    [Fact]
    public void LoadConfig_Returns_Correct_Values()
    {
        Environment.SetEnvironmentVariable("FRAMES_BUCKET_NAME", "fb");
        Environment.SetEnvironmentVariable("ZIP_BUCKET_NAME", "zb");
        Environment.SetEnvironmentVariable("PROGRESS_QUEUE_URL", "pq");
        Environment.SetEnvironmentVariable("MONGO_DB_HOST", "mh");
        Environment.SetEnvironmentVariable("MONGO_DB_USER", "mu");
        Environment.SetEnvironmentVariable("MONGO_DB_PASSWORD", "mp");
        Environment.SetEnvironmentVariable("DATABASE_NAME", "db");
        Environment.SetEnvironmentVariable("COLLECTION_NAME", "col");

        Environment.SetEnvironmentVariable("FRAME_EXTENSION", "png");
        Environment.SetEnvironmentVariable("FRAME_RATE", "2");
        Environment.SetEnvironmentVariable("BLOCK_SIZE", "10");

        var provider = new ConfigProvider();
        var cfg = provider.LoadConfig();

        Assert.Equal("fb", cfg.FramesBucket);
        Assert.Equal("png", cfg.FrameExtension);
        Assert.Equal(2, cfg.FrameRate);
        Assert.Equal(10, cfg.BlockSize);
    }

    [Fact]
    public void LoadConfig_Throws_When_Required_Missing()
    {
        Environment.SetEnvironmentVariable("FRAMES_BUCKET_NAME", null);

        var provider = new ConfigProvider();
        Assert.Throws<InvalidOperationException>(() => provider.LoadConfig());
    }
}
