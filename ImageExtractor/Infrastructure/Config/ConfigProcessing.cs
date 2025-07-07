namespace ImageExtractor.Infrastructure.Config;

public class ConfigProcessing
{
    public string FramesBucket { get; set; } = default!;
    public string ZipBucket { get; set; } = default!;
    public string ProgressQueueUrl { get; set; } = default!;
    public string MongoDbHost { get; set; } = default!;
    public string MongoDbUser { get; set; } = default!;
    public string MongoDbPassword { get; set; } = default!;
    public string DatabaseName { get; set; } = default!;
    public string CollectionName { get; set; } = default!;
    public string TempFolder { get; set; } = default!;
    public string FrameExtension { get; set; } = "jpg";
    public int FrameRate { get; set; } = 1;
    public int BlockSize { get; set; } = 30;
}