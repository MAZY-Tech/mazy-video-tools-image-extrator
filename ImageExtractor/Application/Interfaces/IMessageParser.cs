using Amazon.Lambda.SQSEvents;
using ImageExtractor.Domain;

namespace ImageExtractor.Application.Interfaces;

public interface IMessageParser
{
    ProcessingMessage Parse(SQSEvent.SQSMessage message, IAppLogger logger);
}
