using ImageExtractor.Domain;

namespace ImageExtractor.Application.Interfaces;

public interface IImageExtractionWorkflow
{
    Task ExecuteAsync(IEnumerable<ProcessingMessage> messages, IAppLogger logger);
}
