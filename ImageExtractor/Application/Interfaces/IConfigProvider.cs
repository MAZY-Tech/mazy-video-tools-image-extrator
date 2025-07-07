using ImageExtractor.Infrastructure.Config;

namespace ImageExtractor.Application.Interfaces;

public interface IConfigProvider
{
    ConfigProcessing LoadConfig();
}
