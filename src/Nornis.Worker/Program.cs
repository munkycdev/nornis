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
                .UseAzureMonitor()
                .WithMetrics(metrics => metrics.AddMeter(AiUsageMetrics.MeterName));
        }

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

        var blobConnectionString = configuration["BlobStorage:ConnectionString"];
        if (string.IsNullOrWhiteSpace(blobConnectionString))
            throw new InvalidOperationException(
                "Required configuration 'BlobStorage:ConnectionString' is missing. The worker cannot index library documents without blob storage.");
        services.AddSingleton<IBlobStorageService>(sp =>
            new AzureBlobStorageService(
                blobConnectionString,
                configuration["BlobStorage:ContainerName"] ?? AzureBlobStorageService.DefaultContainerName,
                sp.GetRequiredService<ILogger<AzureBlobStorageService>>()));

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
                options.MaxAutoLockRenewalDuration);
        });

        // Hosted services
        services.AddHostedService<ExtractionWorker>();
        services.AddHostedService<LibraryIndexingWorker>();
    });

var host = builder.Build();
host.Run();
