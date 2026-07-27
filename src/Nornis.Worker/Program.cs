using System.ClientModel;
using Azure.AI.OpenAI;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Nornis.Application.Ai;
using Nornis.Application.Configuration;
using Nornis.Application.Services;
using Nornis.Application.Storage;
using Nornis.Domain.Repositories;
using Nornis.Infrastructure.Ai;
using Nornis.Infrastructure.Messaging;
using Nornis.Infrastructure.Persistence;
using Nornis.Infrastructure.Persistence.Repositories;
using Nornis.Infrastructure.Storage;
using Azure.Monitor.OpenTelemetry.AspNetCore;
using Nornis.Worker;
using Nornis.Worker.Configuration;
using Nornis.Infrastructure.Telemetry;
using OpenAI.Chat;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;

var builder = Host.CreateDefaultBuilder(args)
    .ConfigureServices((context, services) =>
    {
        var configuration = context.Configuration;

        // Observability: Azure Monitor via OpenTelemetry — active only when the
        // deployment provides a connection string; local runs and tests emit nothing.
        if (!string.IsNullOrWhiteSpace(configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"]))
        {
            services.AddOpenTelemetry()
                .ConfigureResource(resource => resource.AddService("nornis-worker"))
                .UseAzureMonitor(options =>
                {
                    // Deliberately NOT sampled down like the API and Web hosts. Those are driven
                    // by UI polling, so their telemetry volume scales with open browser tabs; the
                    // worker's scales with queue depth, which is inherently bounded and low. Its
                    // traces are also the most diagnostically valuable in the system — one record
                    // per extraction, covering a paid AI call that can fail in expensive ways.
                    // Throwing 90% of those away would save almost nothing and cost real
                    // debuggability. The knob exists here so the decision can be revisited if
                    // throughput ever climbs.
                    options.SamplingRatio = configuration.GetValue<float?>("Telemetry:SamplingRatio") ?? 1.0f;
                    options.EnableTraceBasedLogsSampler = true;
                })
                .WithMetrics(metrics => metrics.AddMeter(AiUsageMetrics.MeterName));
        }

        // The worker hosts two independent queue processors. A fault in one — most plausibly a
        // missing or renamed queue, which surfaces as MessagingEntityNotFound out of
        // StartProcessingAsync — must not take the other down with it. The default
        // BackgroundServiceExceptionBehavior.StopHost would stop the entire process, so a
        // library-indexing misconfiguration would silently halt extraction too. Each worker
        // already logs its own failures.
        services.Configure<HostOptions>(options =>
            options.BackgroundServiceExceptionBehavior = BackgroundServiceExceptionBehavior.Ignore);

        // Bind and validate configuration sections
        services.Configure<ExtractionOptions>(configuration.GetSection("Extraction"));
        services.Configure<WorkerOptions>(configuration.GetSection("ServiceBus"));

        // Fail fast: validate required configuration at startup
        var extractionOptions = configuration.GetSection("Extraction").Get<ExtractionOptions>();
        var workerOptions = configuration.GetSection("ServiceBus").Get<WorkerOptions>();

        if (string.IsNullOrWhiteSpace(extractionOptions?.AiModel))
            throw new InvalidOperationException(
                "Required configuration 'Extraction:AiModel' is missing. The worker cannot start without an AI model configured.");

        if (string.IsNullOrWhiteSpace(extractionOptions?.AiEndpoint))
            throw new InvalidOperationException(
                "Required configuration 'Extraction:AiEndpoint' is missing. The worker cannot start without an AI endpoint configured.");

        if (string.IsNullOrWhiteSpace(workerOptions?.ConnectionString))
            throw new InvalidOperationException(
                "Required configuration 'ServiceBus:ConnectionString' is missing. The worker cannot start without a Service Bus connection string configured.");

        // DbContext registration (SQL Server)
        services.AddDbContext<NornisDbContext>(options =>
            options.UseSqlServer(configuration.GetConnectionString("DefaultConnection")));

        // Unit of Work
        services.AddScoped<IUnitOfWork, EfUnitOfWork>();

        // Repository registrations
        services.AddScoped<ISourceRepository, SourceRepository>();
        services.AddScoped<ISourceAttachmentRepository, SourceAttachmentRepository>();
        services.AddScoped<ICampaignRepository, CampaignRepository>();
        services.AddScoped<IWorldRepository, WorldRepository>();
        services.AddScoped<IReviewBatchRepository, ReviewBatchRepository>();
        services.AddScoped<IReviewProposalRepository, ReviewProposalRepository>();
        services.AddScoped<ISourceReferenceRepository, SourceReferenceRepository>();
        services.AddScoped<IAiUsageRecordRepository, AiUsageRecordRepository>();
        services.AddScoped<IArtifactRepository, ArtifactRepository>();
        services.AddScoped<IArtifactFactRepository, ArtifactFactRepository>();
        services.AddScoped<IArtifactRelationshipRepository, ArtifactRelationshipRepository>();
        services.AddScoped<IMapPlacemarkRepository, MapPlacemarkRepository>();
        services.AddScoped<ICharacterRepository, CharacterRepository>();
        services.AddScoped<IExtractionReplayRepository, ExtractionReplayRepository>();
        services.AddScoped<IImportSessionRepository, ImportSessionRepository>();

        // Notifications. The worker is where the asynchronous work actually finishes, so it is
        // the process that has to be able to tell someone about it.
        services.Configure<Nornis.Infrastructure.Notifications.WebPushOptions>(
            context.Configuration.GetSection(Nornis.Infrastructure.Notifications.WebPushOptions.SectionName));
        services.AddScoped<IPushSubscriptionRepository, PushSubscriptionRepository>();
        services.AddScoped<Nornis.Application.Notifications.INotificationSender,
            Nornis.Infrastructure.Notifications.WebPushNotificationSender>();
        services.AddScoped<Nornis.Application.Notifications.IExtractionNotifier,
            Nornis.Application.Notifications.ExtractionNotifier>();

        // Azure OpenAI client
        services.AddSingleton<ChatClient>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<ExtractionOptions>>().Value;
            var endpoint = new Uri(options.AiEndpoint);
            var credential = new ApiKeyCredential(
                configuration["Extraction:AiApiKey"] ?? string.Empty);
            var azureClient = new AzureOpenAIClient(endpoint, credential);
            return azureClient.GetChatClient(options.AiModel);
        });

        // AI extraction client
        services.AddScoped<IAiExtractionClient, AzureOpenAiExtractionClient>();

        // Handwriting transcription (vision) — shares the extraction ChatClient
        services.AddScoped<IHandwritingTranscriptionClient, AzureOpenAiHandwritingTranscriptionClient>();

        // Image lore-reading and map extraction (vision) — same ChatClient
        services.AddScoped<IImageReadingClient, AzureOpenAiImageReadingClient>();
        services.AddScoped<IMapExtractionClient, AzureOpenAiMapExtractionClient>();

        // Daily AI budget guard (checked before every extraction AI call)
        services.Configure<AiBudgetOptions>(configuration.GetSection(AiBudgetOptions.SectionName));
        services.AddScoped<IAiBudgetGuard, AiBudgetGuard>();

        // Extraction service
        services.AddScoped<IExtractionService, ExtractionService>();

        // Timeline replay: a zero-proposal extraction completes with no review step, so
        // the worker itself must be able to advance the walk — which cascades and requeues
        // the next source. That needs the reprocess service and a real queue sender.
        services.AddSingleton(sp =>
        {
            var options = sp.GetRequiredService<IOptions<WorkerOptions>>().Value;
            return new Azure.Messaging.ServiceBus.ServiceBusClient(options.ConnectionString);
        });
        services.AddSingleton<Nornis.Application.Messaging.IExtractionQueueClient, ServiceBusExtractionQueueClient>();
        services.AddScoped<ISourceReprocessService, SourceReprocessService>();
        services.AddScoped<IExtractionReplayService, ExtractionReplayService>();
        services.AddScoped<IExtractionReplayAdvancer>(sp => sp.GetRequiredService<IExtractionReplayService>());

        // Relationship backfill sweep (same queue, ExtractionKind.RelationshipBackfill messages)
        services.AddScoped<IRelationshipBackfillAiClient, AzureOpenAiRelationshipBackfillClient>();
        services.AddScoped<IRelationshipBackfillService, RelationshipBackfillService>();

        // Service Bus extraction processor
        services.AddSingleton<ServiceBusExtractionProcessor>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<WorkerOptions>>().Value;
            return new ServiceBusExtractionProcessor(
                options.ConnectionString,
                options.QueueName,
                options.MaxConcurrentCalls,
                options.PrefetchCount,
                options.MaxAutoLockRenewalDuration);
        });

        // Library indexing: blob storage, PDF text extraction, embeddings, chunk store
        services.Configure<LibraryOptions>(configuration.GetSection(LibraryOptions.SectionName));
        services.AddScoped<ILibraryDocumentRepository, LibraryDocumentRepository>();
        services.AddScoped<ILibraryChunkRepository, LibraryChunkRepository>();
        services.AddSingleton<IPdfTextExtractor, PdfPigTextExtractor>();
        services.AddScoped<ILibraryIndexingService, LibraryIndexingService>();

        // Blob storage is required by library indexing but irrelevant to extraction, so a missing
        // connection string must not be fatal at startup — that would let a library
        // misconfiguration stop extraction, which is the worker's primary job. Register a
        // factory that throws only if something actually resolves it, mirroring the API
        // (src/Nornis.Api/Program.cs). Indexing then fails per-message with a clear error while
        // extraction keeps running.
        var blobConnectionString = configuration["BlobStorage:ConnectionString"];
        if (!string.IsNullOrWhiteSpace(blobConnectionString))
        {
            services.AddSingleton<IBlobStorageService>(sp =>
                new AzureBlobStorageService(
                    blobConnectionString,
                    configuration["BlobStorage:ContainerName"] ?? AzureBlobStorageService.DefaultContainerName,
                    sp.GetRequiredService<ILogger<AzureBlobStorageService>>()));
        }
        else
        {
            services.AddSingleton<IBlobStorageService>(_ =>
                throw new InvalidOperationException(
                    "Blob storage is not configured. Set 'BlobStorage:ConnectionString' to enable library indexing."));
        }

        // Embedding client shares the extraction endpoint/key with the nornis-embed deployment.
        services.AddSingleton<OpenAI.Embeddings.EmbeddingClient>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<ExtractionOptions>>().Value;
            var libraryOptions = sp.GetRequiredService<IOptions<LibraryOptions>>().Value;
            var azureClient = new AzureOpenAIClient(
                new Uri(options.AiEndpoint),
                new ApiKeyCredential(configuration["Extraction:AiApiKey"] ?? string.Empty));
            return azureClient.GetEmbeddingClient(libraryOptions.EmbeddingDeployment);
        });
        services.AddScoped<IEmbeddingClient, AzureOpenAiEmbeddingClient>();

        // Reference-passage retrieval grounds extraction in the world's published library.
        services.AddScoped<Nornis.Application.Knowledge.IReferencePassageRetriever,
            Nornis.Infrastructure.Knowledge.ReferencePassageRetriever>();

        // Second queue processor (keyed): same Service Bus namespace, library-indexing queue.
        services.AddKeyedSingleton<ServiceBusExtractionProcessor>(LibraryIndexingWorker.ProcessorKey, (sp, _) =>
        {
            var options = sp.GetRequiredService<IOptions<WorkerOptions>>().Value;
            return new ServiceBusExtractionProcessor(
                options.ConnectionString,
                ServiceBusLibraryIndexingQueueClient.QueueName,
                options.MaxConcurrentCalls,
                options.PrefetchCount,
                options.LibraryMaxAutoLockRenewalDuration);
        });

        // Hosted services
        services.AddHostedService<ExtractionWorker>();
        services.AddHostedService<LibraryIndexingWorker>();
    });

var host = builder.Build();
host.Run();
