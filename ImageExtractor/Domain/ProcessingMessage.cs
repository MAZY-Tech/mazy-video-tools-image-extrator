namespace ImageExtractor.Domain
{
    public class ProcessingMessage
    {
        public string JobId { get; set; } = default!;

        public string SourceBucket { get; set; } = default!;

        public string SourceKey { get; set; } = default!;
    }
}