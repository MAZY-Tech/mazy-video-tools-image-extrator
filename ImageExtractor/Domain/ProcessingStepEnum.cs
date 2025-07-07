namespace ImageExtractor.Domain;

public enum ProcessingStepEnum
{
    Validating,
    Downloading,
    Analyzing,
    Extracting,
    Zipping,
    Done
}
