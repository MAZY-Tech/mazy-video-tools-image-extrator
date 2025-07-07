using ImageExtractor.Domain;

namespace ImageExtractor.Application.Interfaces;

public interface IJobStateRepository
{
    Task<JobState?> GetJobStateAsync(string jobId);
    Task SaveJobStateAsync(JobState state);
}
