using Amazon.Lambda.Core;
using Amazon.Lambda.SQSEvents;
using Amazon.S3;
using Amazon.S3.Model;
using Amazon.SQS;
using Amazon.SQS.Model;
using MongoDB.Bson;
using MongoDB.Driver;
using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;

[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer))]

namespace ImageExtractor;

public class Function
{
    #region Constantes e Configurações
    // Definimos o tamanho do bloco como 10% da duração total, com um mínimo de 30 segundos.
    private const double BLOCK_PERCENTAGE = 0.10;
    private const int MIN_BLOCK_DURATION_SECONDS = 30;

    private const string FRAME_FILE_PATTERN = "frame_%04d.png";
    #endregion

    #region Propriedades e Clientes AWS
    private readonly IAmazonS3 _s3Client;
    private readonly IAmazonSQS _sqsClient;
    private readonly IMongoClient _mongoClient;
    private readonly IMongoDatabase _database;
    private readonly IMongoCollection<BsonDocument> _jobsCollection;

    // Configurações carregadas do ambiente
    private readonly ConfigProcessamento _config;
    #endregion

    public Function()
    {
        // Inicialização dos clientes AWS
        _s3Client = new AmazonS3Client();
        _sqsClient = new AmazonSQSClient();

        // Carrega e valida configurações
        _config = CarregarConfigs();

        // Configuração do MongoDB
        var mongoSettings = CriarConfigsMongo(_config.MongoDbHost, _config.MongoDbPort, _config.MongoDbUser, _config.MongoDbPassword);

        _mongoClient = new MongoClient(mongoSettings);

        _database = _mongoClient.GetDatabase(_config.DatabaseName);

        _jobsCollection = _database.GetCollection<BsonDocument>(_config.CollectionName);
    }

    public async Task FunctionHandler(SQSEvent sqsEvent, ILambdaContext context)
    {
        // Valida se veio apenas 1 job por vez
        if (sqsEvent.Records.Count != 1)
        {
            context.Logger.LogLine($"ERRO: Recebido {sqsEvent.Records.Count} mensagens, mas apenas 1 job por vez é permitido.");
            throw new ArgumentException($"A função deve processar apenas uma mensagem por invocação. Recebido: {sqsEvent.Records.Count}");
        }

        var sqsRecord = sqsEvent.Records[0];

        await ProcessarMensagem(sqsRecord, context);
    }

    private async Task ProcessarMensagem(SQSEvent.SQSMessage sqsRecord, ILambdaContext context)
    {
        var processingContext = new ContextoProcessamento();

        try
        {
            // Parse e validação da mensagem
            var message = CarregarMensagem(sqsRecord.Body);

            processingContext.InicializarContexto(message);

            context.Logger.LogLine($"Iniciando processamento - Video ID: {message.VideoId}, User: {message.CognitoUserId}");

            // Validação inicial do ambiente
            await ValidarAmbiente(context);

            // Tenta carregar o estado do job do MongoDB para retomar, se aplicável
            // Verifica se o job já está COMPLETED. Se sim, a função BuscarEstadoProcessamento
            // retornará true e esta invocação terminará sem exceção.
            bool jobEstaCompleto = await BuscarEstadoProcessamento(processingContext, context);

            if (jobEstaCompleto)
            {
                context.Logger.LogLine($"Job {processingContext.VideoId} já está como COMPLETED. Ignorando esta mensagem SQS e finalizando a execução com sucesso.");
                return;
            }

            // Executa o pipeline completo de processamento ou retoma
            await ExecutarProcessamento(message, processingContext, context);
        }
        catch (Exception ex)
        {
            await TratarErroProcessamento(processingContext, ex, context);
            throw;
        }
        finally
        {
            await processingContext.LimparTempAsync();
        }
    }

    private async Task ExecutarProcessamento(MensagemSQS message, ContextoProcessamento context, ILambdaContext lambdaContext)
    {
        // Etapa 1: Download do vídeo
        if (context.CurrentStep <= EtapaProcessamento.BaixandoVideo)
        {
            await ExecutarEtapa(EtapaProcessamento.BaixandoVideo, context, lambdaContext, async () =>
            {
                context.VideoDownloadPath = await BaixarVideoS3(context.S3Bucket, context.S3Key, context.VideoId, lambdaContext);

                context.AddArquivoTemp(context.VideoDownloadPath);

                // Atualiza o status no MongoDB para RUNNING se for a primeira vez
                await SalvarEstadoDB(context, EtapaProcessamento.BaixandoVideo, JobStatus.Executando, lambdaContext);

                return new { VideoPath = context.VideoDownloadPath };
            });
        }

        // Etapa 2: Análise do vídeo
        if (context.CurrentStep <= EtapaProcessamento.AnalisandoVideo)
        {
            await ExecutarEtapa(EtapaProcessamento.AnalisandoVideo, context, lambdaContext, async () =>
            {
                var (duration, totalFrames) = await ObterQuantidadeFramesVideo(context.VideoDownloadPath, lambdaContext);
                context.TotalVideoDurationSeconds = duration;
                context.TotalFrames = totalFrames;

                // Calcula blocos
                context.TotalBlocks = (int)Math.Ceiling(context.TotalVideoDurationSeconds * BLOCK_PERCENTAGE / MIN_BLOCK_DURATION_SECONDS);

                if (context.TotalBlocks == 0)
                {
                    context.TotalBlocks = 1; // Pelo menos 1 bloco
                }

                // Atualiza o estado no MongoDB
                await SalvarEstadoDB(context, EtapaProcessamento.AnalisandoVideo, JobStatus.Executando, lambdaContext);
                return new { context.TotalFrames, context.TotalBlocks };
            });
        }

        // Etapa 3: Extração de frames em blocos
        if (context.CurrentStep <= EtapaProcessamento.ExtraindoFrames)
        {
            // Se estiver retomando, ajusta o last_processed_second para o início do próximo bloco
            if (context.CurrentStep == EtapaProcessamento.ExtraindoFrames && context.LastProcessedSecond > 0)
            {
                lambdaContext.Logger.LogLine($"Retomando extração de frames a partir do segundo: {context.LastProcessedSecond}");
            }

            context.FramesOutputDir = CriarPastaFrames(context.VideoId);
            context.AddArquivoTemp(context.FramesOutputDir); // Adiciona o diretório de frames para limpeza no final

            // Lógica de extração de frames granular
            await ExtrairFramesPorBloco(context, lambdaContext);

            // Atualiza o estado para COMPLETED após a extração de todos os blocos
            await SalvarEstadoDB(context, EtapaProcessamento.ExtraindoFrames, JobStatus.Executando, lambdaContext);
        }

        // Etapa 4: Criação e upload do ZIP
        if (context.CurrentStep <= EtapaProcessamento.CriandoZIP)
        {
            await ExecutarEtapa(EtapaProcessamento.CriandoZIP, context, lambdaContext, async () =>
            {
                context.ZipPath = await CriarEnviarZip(context, lambdaContext);
                var zipS3Key = $"{message.CognitoUserId}/{message.VideoId}.zip";

                return new { ZipS3Key = zipS3Key, _config.ZipBucket };
            });
        }

        // Etapa final: Conclusão
        await ExecutarEtapa(EtapaProcessamento.Concluido, context, lambdaContext, async () =>
        {
            var zipS3Key = $"{message.CognitoUserId}/{message.VideoId}.zip";

            // Atualiza o estado final no MongoDB para COMPLETED
            await SalvarEstadoDB(context, EtapaProcessamento.Concluido, JobStatus.Completo, lambdaContext);

            // Envia mensagem de conclusão para a fila de progresso
            await EnviarMensagemConclusao(message, zipS3Key, context.TotalFrames, lambdaContext);

            lambdaContext.Logger.LogLine($"Processamento completo para Video ID: {message.VideoId}. Total de frames processados: {context.ProcessedFrames}");
            return new
            {
                Status = "COMPLETED",
                CompletedAt = DateTime.UtcNow,
                OriginalS3File = $"s3://{message.Bucket}/{message.Key}"
            };
        });
    }

    private async Task ExecutarEtapa(EtapaProcessamento step, ContextoProcessamento context, ILambdaContext lambdaContext,
        Func<Task<object>> stepLogic)
    {
        context.CurrentStep = step;
        var stepName = step.ToString();

        lambdaContext.Logger.LogLine($"Executando: {stepName} (Video ID: {context.VideoId})");

        // Executa a lógica da etapa
        _ = await stepLogic();

        lambdaContext.Logger.LogLine($"Completado: {stepName}");
    }

    #region Métodos de Configuração e Validação
    private static ConfigProcessamento CarregarConfigs()
    {
        var config = new ConfigProcessamento
        {
            FramesBucket = Environment.GetEnvironmentVariable("FRAMES_BUCKET_NAME") ?? string.Empty,
            ZipBucket = Environment.GetEnvironmentVariable("ZIP_BUCKET_NAME") ?? string.Empty,
            ProgressQueueUrl = Environment.GetEnvironmentVariable("PROGRESS_QUEUE_URL") ?? string.Empty,
            MongoDbHost = Environment.GetEnvironmentVariable("MONGO_DB_HOST") ?? string.Empty,
            MongoDbPort = int.Parse(Environment.GetEnvironmentVariable("MONGO_DB_PORT") ?? "27017"),
            MongoDbUser = Environment.GetEnvironmentVariable("MONGO_DB_USER") ?? string.Empty,
            MongoDbPassword = Environment.GetEnvironmentVariable("MONGO_DB_PASSWORD") ?? string.Empty,
            DatabaseName = Environment.GetEnvironmentVariable("DATABASE_NAME") ?? "video_processing",
            CollectionName = Environment.GetEnvironmentVariable("COLLECTION_NAME") ?? "jobs"
        };

        if (!config.ConfigValida())
        {
            throw new InvalidOperationException("Configuração incompleta. Verifique as variáveis de ambiente da Lambda.");
        }

        return config;
    }

    private MongoClientSettings CriarConfigsMongo(string host, int port, string user, string password)
    {
        // Construção da connection string do MongoDB
        var connectionString = $"mongodb://{user}:{password}@{host}:{port}/{_config.DatabaseName}?authSource=admin"; // TODO: Verificar se authSource é necessário
        var settings = MongoClientSettings.FromConnectionString(connectionString);

        // Configurações otimizadas para Lambda
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

    private async Task ValidarAmbiente(ILambdaContext context)
    {
        var validationTasks = new[]
        {
            Task.Run(ValidarPastaTemp),
            Task.Run(ValidarFFmpeg),
            TestarConexaoMongoDb()
        };

        await Task.WhenAll(validationTasks);
        context.Logger.LogLine("Ambiente validado com sucesso");
    }

    private void ValidarPastaTemp()
    {
        var tempDir = Path.GetTempPath();

        if (!Directory.Exists(tempDir))
        {
            throw new DirectoryNotFoundException($"Diretório temporário inacessível: {tempDir}");
        }
    }

    private void ValidarFFmpeg()
    {
        if (!File.Exists("./ffprobe") || !File.Exists("./ffmpeg"))
        {
            throw new FileNotFoundException("Binários FFmpeg não encontrados no diretório de execução. Verifique se foram incluídos no pacote de deploy.");
        }
    }

    private async Task TestarConexaoMongoDb()
    {
        using var cancellationTokenSource = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        try
        {
            await _database.RunCommandAsync<BsonDocument>(
                new BsonDocument("ping", 1),
                cancellationToken: cancellationTokenSource.Token);
        }
        catch (MongoException ex)
        {
            throw new Exception($"Falha ao conectar ao MongoDB: {ex.Message}", ex);
        }
    }
    #endregion

    #region Métodos de Download e Upload
    private async Task<string> BaixarVideoS3(string bucketName, string key, string videoId, ILambdaContext context)
    {
        var extension = Path.GetExtension(key);
        var tempDir = Path.GetTempPath();
        var downloadPath = Path.Combine(tempDir, $"{videoId}{extension}");

        var request = new GetObjectRequest
        {
            BucketName = bucketName,
            Key = key
        };

        context.Logger.LogLine($"Iniciando download do vídeo s3://{bucketName}/{key} para {downloadPath}");

        using var response = await _s3Client.GetObjectAsync(request);

        // Verifica se o objeto existe e é acessível
        if (response.HttpStatusCode != System.Net.HttpStatusCode.OK)
        {
            throw new Exception($"Falha no download: HTTP {response.HttpStatusCode} para {bucketName}/{key}");
        }

        // Download com progresso
        using var fileStream = new FileStream(downloadPath, FileMode.Create, FileAccess.Write, FileShare.None,
            bufferSize: 65536, useAsync: true);

        await response.ResponseStream.CopyToAsync(fileStream);

        context.Logger.LogLine($"Vídeo baixado: {downloadPath} ({new FileInfo(downloadPath).Length} bytes)");
        return downloadPath;
    }

    private async Task EnviarFramesBucket(string localPath, string s3Prefix, ILambdaContext context)
    {
        var frameFiles = Directory.GetFiles(localPath, "*.png");

        if (frameFiles.Length == 0)
        {
            throw new FileNotFoundException($"Nenhum frame encontrado para upload no diretório: {localPath}");
        }

        context.Logger.LogLine($"Iniciando upload de {frameFiles.Length} frames do diretório: {localPath}");

        // Upload paralelo com controle de concorrência
        var semaphore = new SemaphoreSlim(5, 5); // Limita a 5 uploads simultâneos

        var uploadTasks = frameFiles.Select(async filePath =>
        {
            await semaphore.WaitAsync();

            try
            {
                var fileName = Path.GetFileName(filePath);
                var key = $"{s3Prefix}{fileName}";

                await _s3Client.PutObjectAsync(new PutObjectRequest
                {
                    BucketName = _config.FramesBucket,
                    Key = key,
                    FilePath = filePath
                });
            }
            finally
            {
                semaphore.Release();
            }
        });

        await Task.WhenAll(uploadTasks);
        context.Logger.LogLine($"Upload concluído: {frameFiles.Length} frames");
    }

    #endregion

    #region Métodos FFmpeg
    private static async Task<(int durationSeconds, int totalFrames)> ObterQuantidadeFramesVideo(string videoPath, ILambdaContext context)
    {
        var processInfo = new ProcessStartInfo
        {
            FileName = "./ffprobe",
            Arguments = $"-v quiet -print_format json -show_format -show_streams \"{videoPath}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = new Process { StartInfo = processInfo };
        process.Start();

        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();

        await process.WaitForExitAsync();

        if (process.ExitCode != 0)
        {
            var error = await errorTask;
            throw new Exception($"FFprobe falhou (exit code {process.ExitCode}): {error}");
        }

        var output = await outputTask;
        var metadata = JsonSerializer.Deserialize<JsonElement>(output);

        int durationSeconds = 0;
        int totalFrames = 0;

        if (metadata.TryGetProperty("format", out var format) &&
            format.TryGetProperty("duration", out var durationElement))
        {
            durationSeconds = (int)Math.Ceiling(durationElement.GetDouble());
        }
        else
        {
            throw new Exception("Não foi possível determinar a duração do vídeo.");
        }

        if (metadata.TryGetProperty("streams", out var streams))
        {
            foreach (var stream in streams.EnumerateArray())
            {
                if (stream.TryGetProperty("codec_type", out var codecType) && codecType.GetString() == "video")
                {
                    if (stream.TryGetProperty("nb_frames", out var nbFramesElement))
                    {
                        if (int.TryParse(nbFramesElement.GetString(), out totalFrames))
                        {
                            break; // Encontrou o stream de vídeo e o número de frames
                        }
                    }
                }
            }
        }

        // Fallback para totalFrames se nb_frames não estiver disponível
        if (totalFrames == 0 && durationSeconds > 0)
        {
            // Assume 1 frame por segundo se não conseguir extrair o nb_frames
            totalFrames = durationSeconds;
            context.Logger.LogLine("Aviso: 'nb_frames' não encontrado, estimando total de frames como duração em segundos.");
        }

        context.Logger.LogLine($"Duração do vídeo: {durationSeconds} segundos, Total de frames: {totalFrames}");
        return (durationSeconds, totalFrames);
    }

    private async Task ExtrairFramesPorBloco(ContextoProcessamento context, ILambdaContext lambdaContext)
    {
        var blockDuration = (int)Math.Max(MIN_BLOCK_DURATION_SECONDS, context.TotalVideoDurationSeconds * BLOCK_PERCENTAGE);
        context.TotalBlocks = (int)Math.Ceiling((double)context.TotalVideoDurationSeconds / blockDuration);

        if (context.TotalBlocks == 0)
        {
            context.TotalBlocks = 1; // Garante pelo menos 1 bloco
        }

        lambdaContext.Logger.LogLine($"Extraindo frames em {context.TotalBlocks} blocos de aproximadamente {blockDuration} segundos cada.");

        int startSecond = context.LastProcessedSecond; // Retoma do último segundo processado

        for (int i = 0; i < context.TotalBlocks; i++)
        {
            context.CurrentBlock = i + 1; // Bloco atual (base 1)
            int blockStartSecond = startSecond;
            int blockEndSecond = Math.Min(context.TotalVideoDurationSeconds, startSecond + blockDuration);

            // Se for a primeira execução do bloco, ou se o bloco anterior não foi concluído
            if (context.LastProcessedSecond <= blockStartSecond)
            {
                lambdaContext.Logger.LogLine($"Iniciando processamento do Bloco {context.CurrentBlock}/{context.TotalBlocks}: Segundos {blockStartSecond}-{blockEndSecond}");

                // Comando FFmpeg para extrair frames de um intervalo específico
                var processInfo = new ProcessStartInfo
                {
                    FileName = "./ffmpeg",
                    Arguments = $"-threads 0 -hwaccel auto -ss {blockStartSecond} -i \"{context.VideoDownloadPath}\" -to {blockEndSecond - blockStartSecond} -vf fps=1 -q:v 5 -f image2 -y \"{context.FramesOutputDir}/{FRAME_FILE_PATTERN}\"",
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = new Process { StartInfo = processInfo, EnableRaisingEvents = true };

                process.Start();
                process.BeginErrorReadLine();

                var timeoutTask = Task.Delay(TimeSpan.FromMinutes(10));
                var processTask = process.WaitForExitAsync();

                var completedTask = await Task.WhenAny(processTask, timeoutTask);

                if (completedTask == timeoutTask)
                {
                    process.Kill();
                    throw new TimeoutException($"Extração de frames para o Bloco {context.CurrentBlock} excedeu o timeout.");
                }

                if (process.ExitCode != 0)
                {
                    throw new Exception($"FFmpeg falhou na extração do Bloco {context.CurrentBlock} (exit code {process.ExitCode})");
                }

                lambdaContext.Logger.LogLine($"Extração concluída para o Bloco {context.CurrentBlock}.");
            }
            else
            {
                lambdaContext.Logger.LogLine($"Bloco {context.CurrentBlock} já processado, pulando.");
            }

            // Após a extração do bloco, faz o upload dos frames gerados em um subdiretório específico do bloco
            var s3Prefix = $"{context.CognitoUserId}/{context.VideoId}/block_{context.CurrentBlock}/";
            var blockFramesDir = Path.Combine(context.FramesOutputDir, $"block_{context.CurrentBlock}");

            if (!Directory.Exists(blockFramesDir))
            {
                Directory.CreateDirectory(blockFramesDir);
            }

            // Mover os frames do diretório principal para o subdiretório do bloco
            foreach (var frameFile in Directory.GetFiles(context.FramesOutputDir, "*.png"))
            {
                var fileName = Path.GetFileName(frameFile);
                File.Move(frameFile, Path.Combine(blockFramesDir, fileName));
            }

            await EnviarFramesBucket(blockFramesDir, s3Prefix, lambdaContext);

            context.AddArquivoTemp(blockFramesDir); // Adiciona a pasta do bloco para limpeza no final

            // Atualiza o progresso no contexto
            context.LastProcessedSecond = blockEndSecond;
            context.ProcessedFrames += Directory.GetFiles(blockFramesDir, "*.png").Length; // Atualiza com os frames do bloco

            // Envia mensagem de progresso para a fila de progresso
            await EnviarMensagemProgresso(context, lambdaContext);

            // Atualiza o estado no MongoDB após cada bloco
            await SalvarEstadoDB(context, EtapaProcessamento.ExtraindoFrames, JobStatus.Executando, lambdaContext);

            startSecond = blockEndSecond + 1; // Opa, comece do segundo seguinte do segundo final do bloco atual
        }

        lambdaContext.Logger.LogLine($"Extração de frames concluída para todos os blocos. Total de frames extraídos: {context.ProcessedFrames}");
    }

    #endregion

    #region Métodos do MongoDB
    private async Task<bool> BuscarEstadoProcessamento(ContextoProcessamento context, ILambdaContext lambdaContext)
    {
        var filter = Builders<BsonDocument>.Filter.Eq("_id", context.VideoId);
        var jobDocument = await _jobsCollection.Find(filter).FirstOrDefaultAsync();

        if (jobDocument != null)
        {
            lambdaContext.Logger.LogLine($"Estado do job encontrado no MongoDB para Video ID: {context.VideoId}");

            // Recupera o status e outros dados relevantes
            if (jobDocument.TryGetValue("status", out var statusBson) && Enum.TryParse<JobStatus>(statusBson.AsString, true, out var jobStatus))
            {
                context.CurrentJobStatus = jobStatus;
            }

            if (jobDocument.TryGetValue("current_step", out var currentStepBson) && Enum.TryParse<EtapaProcessamento>(currentStepBson.AsString, true, out var processingStep))
            {
                context.CurrentStep = processingStep;
            }

            if (jobDocument.TryGetValue("last_processed_second", out var lastProcessedSecondBson))
            {
                context.LastProcessedSecond = lastProcessedSecondBson.AsInt32;
            }

            if (jobDocument.TryGetValue("total_frames", out var totalFramesBson))
            {
                context.TotalFrames = totalFramesBson.AsInt32;
            }

            if (jobDocument.TryGetValue("processed_frames", out var processedFramesBson))
            {
                context.ProcessedFrames = processedFramesBson.AsInt32;
            }

            if (jobDocument.TryGetValue("current_block", out var currentBlockBson))
            {
                context.CurrentBlock = currentBlockBson.AsInt32;
            }

            if (jobDocument.TryGetValue("total_blocks", out var totalBlocksBson))
            {
                context.TotalBlocks = totalBlocksBson.AsInt32;
            }

            // De acordo com o CurrentJobStatus, determina o que fazer
            if (context.CurrentJobStatus == JobStatus.Completo)
            {
                lambdaContext.Logger.LogLine($"Aviso: Job {context.VideoId} já está COMPLETED. Ignorando esta mensagem SQS.");
                return true; // Retorna true para indicar que o job já foi concluído e não precisa de reprocessamento
            }
            // Se o job está Falhou, Interrompido ou Executando, ele pode ser retomado.
            else if (context.CurrentJobStatus == JobStatus.Falhou ||
                     context.CurrentJobStatus == JobStatus.Interrompido ||
                     context.CurrentJobStatus == JobStatus.Executando)
            {
                lambdaContext.Logger.LogLine($"Retomando processamento do Job {context.VideoId} na etapa '{context.CurrentStep}' a partir do segundo {context.LastProcessedSecond}.");
            }
        }
        else
        {
            lambdaContext.Logger.LogLine($"Nenhum estado de job encontrado no MongoDB para Video ID: {context.VideoId}. Iniciando novo processamento.");
            // Inicializa o job no MongoDB como PENDING
            await SalvarEstadoDB(context, EtapaProcessamento.Validando, JobStatus.Pendente, lambdaContext);
        }
        return false; // Job não está COMPLETED
    }

    private async Task SalvarEstadoDB(ContextoProcessamento context, EtapaProcessamento currentStep, JobStatus jobStatus, ILambdaContext lambdaContext)
    {
        var document = new BsonDocument
        {
            ["_id"] = context.VideoId, // Usa video_id como _id
            ["status"] = jobStatus.ToString(),
            ["progress"] = context.TotalFrames > 0 ? (int)((double)context.ProcessedFrames / context.TotalFrames * 100) : 0,
            ["total_frames"] = context.TotalFrames,
            ["processed_frames"] = context.ProcessedFrames,
            ["last_processed_second"] = context.LastProcessedSecond,
            ["current_block"] = context.CurrentBlock,
            ["total_blocks"] = context.TotalBlocks,
            ["last_heartbeat"] = DateTime.UtcNow,
            ["current_step"] = currentStep.ToString()
        };

        // Adiciona started_at apenas na primeira inserção
        if (jobStatus == JobStatus.Pendente || jobStatus == JobStatus.Executando) // Usando o novo enum
        {
            var existingJob = await _jobsCollection.Find(Builders<BsonDocument>.Filter.Eq("_id", context.VideoId)).Project(Builders<BsonDocument>.Projection.Include("started_at")).FirstOrDefaultAsync();
            if (existingJob == null || !existingJob.TryGetElement("started_at", out _))
            {
                document["started_at"] = DateTime.UtcNow;
            }
            else
            {
                document["started_at"] = existingJob["started_at"]; // Mantém o started_at original
            }
            document["completed_at"] = BsonNull.Value; // Garante que completed_at seja null enquanto processando
        }
        else if (jobStatus == JobStatus.Completo) // Usando o novo enum
        {
            document["completed_at"] = DateTime.UtcNow;
            // Se já tiver started_at, mantém
            var existingJob = await _jobsCollection.Find(Builders<BsonDocument>.Filter.Eq("_id", context.VideoId)).Project(Builders<BsonDocument>.Projection.Include("started_at")).FirstOrDefaultAsync();
            if (existingJob != null && existingJob.TryGetElement("started_at", out _))
            {
                document["started_at"] = existingJob["started_at"];
            }
        }
        else if (jobStatus == JobStatus.Falhou || jobStatus == JobStatus.Interrompido) // Usando o novo enum
        {
            // Se já tiver started_at, mantém
            var existingJob = await _jobsCollection.Find(Builders<BsonDocument>.Filter.Eq("_id", context.VideoId)).Project(Builders<BsonDocument>.Projection.Include("started_at")).FirstOrDefaultAsync();
            if (existingJob != null && existingJob.TryGetElement("started_at", out _))
            {
                document["started_at"] = existingJob["started_at"];
            }
            document["completed_at"] = BsonNull.Value; // Não concluído
        }

        var options = new ReplaceOptions { IsUpsert = true }; // Insere ou atualiza
        await _jobsCollection.ReplaceOneAsync(
            Builders<BsonDocument>.Filter.Eq("_id", context.VideoId),
            document,
            options
        );
        lambdaContext.Logger.LogLine($"Estado do job {context.VideoId} salvo no MongoDB. Status: {jobStatus}, Bloco: {context.CurrentBlock}/{context.TotalBlocks}, Segundo: {context.LastProcessedSecond}");
    }

    #endregion

    #region Métodos Utilitários
    private static MensagemSQS CarregarMensagem(string messageBody)
    {
        if (string.IsNullOrWhiteSpace(messageBody))
        {
            throw new ArgumentException("Corpo da mensagem SQS está vazio");
        }

        MensagemSQS? message;

        try
        {
            message = JsonSerializer.Deserialize<MensagemSQS>(messageBody);
        }
        catch (JsonException ex)
        {
            throw new ArgumentException($"Mensagem SQS inválida: {ex.Message}");
        }

        return message == null || !message.MensagemValida()
            ? throw new ArgumentException("Mensagem SQS contém dados inválidos ou incompletos")
            : message;
    }

    private static string CriarPastaFrames(string videoId)
    {
        var tempDir = Path.GetTempPath();
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var framesDir = Path.Combine(tempDir, $"{videoId}_frames_{timestamp}");

        Directory.CreateDirectory(framesDir);

        return framesDir;
    }
    #endregion

    #region Método de Criação e Upload de ZIP
    private async Task<string> CriarEnviarZip(ContextoProcessamento context, ILambdaContext lambdaContext)
    {
        var zipPath = Path.Combine(Path.GetTempPath(), $"{context.VideoId}_{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}.zip");
        context.AddArquivoTemp(zipPath);

        lambdaContext.Logger.LogLine($"Iniciando criação do ZIP em: {zipPath}");

        var listObjectsRequest = new ListObjectsV2Request
        {
            BucketName = _config.FramesBucket,
            Prefix = $"{context.CognitoUserId}/{context.VideoId}/" // Prefix para todos os frames do job
        };

        List<S3Object> allFrameObjects = [];

        ListObjectsV2Response listObjectsResponse;

        do
        {
            listObjectsResponse = await _s3Client.ListObjectsV2Async(listObjectsRequest);
            allFrameObjects.AddRange(listObjectsResponse.S3Objects);
            listObjectsRequest.ContinuationToken = listObjectsResponse.NextContinuationToken;
        }
        while (listObjectsResponse.IsTruncated == true);

        if (allFrameObjects.Count == 0)
        {
            throw new InvalidOperationException($"Nenhum frame encontrado no bucket {_config.FramesBucket} com prefixo {context.CognitoUserId}/{context.VideoId}/ para criar o ZIP.");
        }

        using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            foreach (var s3Object in allFrameObjects)
            {
                var getObjectRequest = new GetObjectRequest
                {
                    BucketName = _config.FramesBucket,
                    Key = s3Object.Key
                };

                using var response = await _s3Client.GetObjectAsync(getObjectRequest);
                using var responseStream = response.ResponseStream;

                // Usa o nome do arquivo a partir da key S3
                var entryName = Path.GetFileName(s3Object.Key);
                var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);

                using var entryStream = entry.Open();
                await responseStream.CopyToAsync(entryStream);
            }
        }

        var zipInfo = new FileInfo(zipPath);
        lambdaContext.Logger.LogLine($"ZIP criado: {zipPath} ({zipInfo.Length} bytes)");

        // Upload do ZIP para S3
        var zipS3Key = $"{context.CognitoUserId}/{context.VideoId}.zip";

        await _s3Client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = _config.ZipBucket,
            Key = zipS3Key,
            FilePath = zipPath,
            ServerSideEncryptionMethod = ServerSideEncryptionMethod.AES256,
            ContentType = "application/zip"
        });

        lambdaContext.Logger.LogLine($"ZIP enviado para S3: {_config.ZipBucket}/{zipS3Key}");
        return zipPath;
    }
    #endregion

    #region Métodos de Notificação e Tratamento de Erros
    private async Task EnviarMensagemProgresso(ContextoProcessamento context, ILambdaContext lambdaContext)
    {
        var progress = context.TotalFrames > 0 ? (int)((double)context.ProcessedFrames / context.TotalFrames * 100) : 0;

        var messageBody = JsonSerializer.Serialize(new
        {
            video_id = context.VideoId,
            status = "RUNNING",
            progress = progress,
            current_block = context.CurrentBlock,
            total_blocks = context.TotalBlocks,
            timestamp = DateTime.UtcNow
        });

        await _sqsClient.SendMessageAsync(new SendMessageRequest
        {
            QueueUrl = _config.ProgressQueueUrl,
            MessageBody = messageBody
        });

        lambdaContext.Logger.LogLine($"Mensagem de progresso enviada para fila: {progress}% (Bloco: {context.CurrentBlock}/{context.TotalBlocks})");
    }

    private async Task EnviarMensagemConclusao(MensagemSQS originalMessage, string zipS3Key, int totalFrames, ILambdaContext lambdaContext)
    {
        var messageBody = JsonSerializer.Serialize(new
        {
            video_id = originalMessage.VideoId,
            status = "COMPLETED",
            progress = 100,
            timestamp = DateTime.UtcNow,
            zip = new
            {
                bucket = _config.ZipBucket,
                key = zipS3Key
            }
        });

        await _sqsClient.SendMessageAsync(new SendMessageRequest
        {
            QueueUrl = _config.ProgressQueueUrl,
            MessageBody = messageBody
        });
        lambdaContext.Logger.LogLine($"Mensagem de conclusão enviada para fila para Video ID: {originalMessage.VideoId}");
    }

    private async Task TratarErroProcessamento(ContextoProcessamento context, Exception exception, ILambdaContext lambdaContext)
    {
        // Log detalhado do erro
        lambdaContext.Logger.LogLine($"ERRO CRÍTICO - Video ID: {context.VideoId}, Etapa: {context.CurrentStep}");
        lambdaContext.Logger.LogLine($"Erro: {exception.Message}");
        lambdaContext.Logger.LogLine($"Stack Trace: {exception.StackTrace}");

        // Salva estado de erro no banco como FAILED
        try
        {
            // O status agora é FAILED e a etapa é a que falhou (context.CurrentStep)
            await SalvarEstadoDB(context, context.CurrentStep, JobStatus.Falhou, lambdaContext);
        }
        catch (Exception dbEx)
        {
            lambdaContext.Logger.LogLine($"Falha ao salvar estado de erro no DB: {dbEx.Message}");
        }

        lambdaContext.Logger.LogLine("Erro tratado. A mensagem SQS será devolvida à fila para retries ou movida para a DLQ conforme configuração do SQS.");
    }

    #endregion

    #region Classes de Contexto e Configuração
    private class ContextoProcessamento
    {
        public string VideoId { get; private set; } = string.Empty;
        public string CognitoUserId { get; private set; } = string.Empty;
        public string S3Bucket { get; private set; } = string.Empty;
        public string S3Key { get; private set; } = string.Empty;
        public EtapaProcessamento CurrentStep { get; set; } = EtapaProcessamento.Validando;
        public JobStatus CurrentJobStatus { get; set; } = JobStatus.Pendente;

        public string VideoDownloadPath { get; set; } = string.Empty;
        public string FramesOutputDir { get; set; } = string.Empty;
        public string ZipPath { get; set; } = string.Empty;
        public int TotalFrames { get; set; }
        public int TotalVideoDurationSeconds { get; set; }
        public int LastProcessedSecond { get; set; } = 0; // Novo campo para retomar
        public int CurrentBlock { get; set; } = 0; // Novo campo
        public int TotalBlocks { get; set; } = 0; // Novo campo
        public int ProcessedFrames { get; set; } = 0; // Novo campo para controle de progresso

        private readonly List<string> _tempFilesToCleanup = new();

        public void InicializarContexto(MensagemSQS message)
        {
            VideoId = message.VideoId;
            CognitoUserId = message.CognitoUserId;
            S3Bucket = message.Bucket;
            S3Key = message.Key;
        }

        public void AddArquivoTemp(string path)
        {
            if (!string.IsNullOrEmpty(path))
            {
                _tempFilesToCleanup.Add(path);
            }
        }

        public async Task LimparTempAsync()
        {
            var cleanupTasks = _tempFilesToCleanup.Select(static async path =>
            {
                await Task.CompletedTask;

                try
                {
                    if (File.Exists(path))
                    {
                        File.Delete(path);
                    }
                    else if (Directory.Exists(path))
                    {
                        Directory.Delete(path, true);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Aviso: Falha na limpeza de {path}: {ex.Message}");
                }
            });

            await Task.WhenAll(cleanupTasks);
        }
    }

    public class MensagemSQS
    {
        [JsonPropertyName("video_id")]
        public string VideoId { get; set; } = string.Empty;
        [JsonPropertyName("video_hash")]
        public string VideoHash { get; set; } = string.Empty;
        [JsonPropertyName("cognito_user_id")]
        public string CognitoUserId { get; set; } = string.Empty;
        [JsonPropertyName("bucket")]
        public string Bucket { get; set; } = string.Empty;
        [JsonPropertyName("key")]
        public string Key { get; set; } = string.Empty;
        [JsonPropertyName("timestamp")]
        public DateTime Timestamp { get; set; }

        public bool MensagemValida() =>
            !string.IsNullOrWhiteSpace(VideoId) &&
            !string.IsNullOrWhiteSpace(CognitoUserId) &&
            !string.IsNullOrWhiteSpace(Bucket) &&
            !string.IsNullOrWhiteSpace(Key);
    }

    public class ConfigProcessamento
    {
        public string FramesBucket { get; set; } = string.Empty;
        public string ZipBucket { get; set; } = string.Empty;
        public string ProgressQueueUrl { get; set; } = string.Empty;
        public string MongoDbHost { get; set; } = string.Empty;
        public int MongoDbPort { get; set; }
        public string MongoDbUser { get; set; } = string.Empty;
        public string MongoDbPassword { get; set; } = string.Empty;
        public string DatabaseName { get; set; } = "video_processing";
        public string CollectionName { get; set; } = "jobs";

        public bool ConfigValida() =>
            !string.IsNullOrWhiteSpace(FramesBucket) &&
            !string.IsNullOrWhiteSpace(ZipBucket) &&
            !string.IsNullOrWhiteSpace(ProgressQueueUrl) &&
            !string.IsNullOrWhiteSpace(MongoDbHost) &&
            MongoDbPort > 0 &&
            !string.IsNullOrWhiteSpace(MongoDbUser) &&
            !string.IsNullOrWhiteSpace(MongoDbPassword);
    }

    public enum EtapaProcessamento
    {
        Validando,
        BaixandoVideo,
        AnalisandoVideo,
        ExtraindoFrames,
        CriandoZIP,
        Concluido
    }

    public enum JobStatus
    {
        Pendente,
        Executando,
        Completo,
        Falhou,
        Interrompido
    }
    #endregion
}