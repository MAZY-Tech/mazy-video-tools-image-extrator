namespace ImageExtractor.Domain;

public enum ProcessingStep
{
    Validating,
    Downloading,
    Analyzing,
    Extracting,
    Zipping,
    Done
}
