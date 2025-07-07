using ImageExtractor.Application.Interfaces;
using ImageExtractor.Domain;
using ImageExtractor.Infrastructure.Config;
using MongoDB.Driver;
using System.ComponentModel;

namespace ImageExtractor.Infrastructure.Repositories;

public class MongoJobStateRepository : IJobStateRepository
{
    private readonly IMongoCollection<JobStateDocument> _collection;

    public MongoJobStateRepository(ConfigProcessing config)
    {
        var mongoSettings = CreateMongoSettings(config.MongoDbHost, config.MongoDbUser, config.MongoDbPassword, config.DatabaseName);

        try
        {
            var client = new MongoClient(mongoSettings);

            var database = client.GetDatabase(config.DatabaseName);

            _collection = database.GetCollection<JobStateDocument>(config.CollectionName);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Error on create MongoDB client: " + ex.Message, ex);
        }
    }

    [EditorBrowsable(EditorBrowsableState.Never)]
    public MongoJobStateRepository(IMongoCollection<JobStateDocument> collection) => _collection = collection;

    public async Task<JobState?> GetJobStateAsync(string jobId)
    {
        var filter = Builders<JobStateDocument>.Filter.Eq(j => j.JobId, jobId);

        var cursor = await _collection.FindAsync(filter);
        var document = await cursor.FirstOrDefaultAsync();

        if (document == null)
        {
            return null;
        }

        return ToDomain(document);
    }

    public async Task SaveJobStateAsync(JobState state)
    {
        var document = ToDocument(state);

        var filter = Builders<JobStateDocument>.Filter.Eq(j => j.JobId, document.JobId);
        var options = new ReplaceOptions { IsUpsert = true };

        await _collection.ReplaceOneAsync(filter, document, options);
    }

    private static MongoClientSettings CreateMongoSettings(string host, string user, string password, string databaseName)
    {
        var connectionString = $"mongodb+srv://{user}:{password}@{host}/{databaseName}";
        var settings = MongoClientSettings.FromConnectionString(connectionString);

        settings.MaxConnectionPoolSize = 10;
        settings.MinConnectionPoolSize = 1;
        settings.MaxConnectionIdleTime = TimeSpan.FromMinutes(5);
        settings.ConnectTimeout = TimeSpan.FromSeconds(10);
        settings.ServerSelectionTimeout = TimeSpan.FromSeconds(10);

        settings.SslSettings = new SslSettings
        {
            EnabledSslProtocols = System.Security.Authentication.SslProtocols.Tls12
        };

        return settings;
    }

    private static JobState ToDomain(JobStateDocument doc)
    {
        return new JobState
        {
            JobId = doc.JobId,
            Status = Enum.Parse<JobStatusEnum>(doc.Status),
            CurrentStep = Enum.Parse<ProcessingStepEnum>(doc.CurrentStep),
            LastProcessedSecond = doc.LastProcessedSecond,
            CurrentBlock = doc.CurrentBlock,
            TotalBlocks = doc.TotalBlocks,
            ProcessedFrames = doc.ProcessedFrames,
            TotalFrames = doc.TotalFrames,
            Progress = doc.Progress,
            CreatedAt = doc.CreatedAt,
            StartedAt = doc.StartedAt,
            CompletedAt = doc.CompletedAt,
            LastHeartbeat = doc.LastHeartbeat,
            Metadata = doc.Metadata ?? []
        };
    }

    private static JobStateDocument ToDocument(JobState state)
    {
        return new JobStateDocument
        {
            JobId = state.JobId,
            Status = state.Status.ToString(),
            CurrentStep = state.CurrentStep.ToString(),
            LastProcessedSecond = state.LastProcessedSecond,
            CurrentBlock = state.CurrentBlock,
            TotalBlocks = state.TotalBlocks,
            ProcessedFrames = state.ProcessedFrames,
            TotalFrames = state.TotalFrames,
            Progress = state.Progress,
            CreatedAt = state.CreatedAt,
            StartedAt = state.StartedAt,
            CompletedAt = state.CompletedAt,
            LastHeartbeat = state.LastHeartbeat,
            Metadata = state.Metadata ?? []
        };
    }
}
