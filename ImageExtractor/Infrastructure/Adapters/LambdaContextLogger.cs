using Amazon.Lambda.Core;
using ImageExtractor.Application.Interfaces;

namespace ImageExtractor.Infrastructure.Adapters;

public class LambdaContextLogger(ILambdaLogger lambdaLogger) : IAppLogger
{
    public void Log(string message) => lambdaLogger.LogLine(message);
}
