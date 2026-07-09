using System.ClientModel;
using Azure.AI.OpenAI;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Nornis.Application.Ai;
using Nornis.Application.Configuration;
using Nornis.Application.Services;
using Nornis.Domain.Repositories;
using Nornis.Infrastructure.Ai;
using Nornis.Infrastructure.Messaging;
using Nornis.Infrastructure.Persistence;
using Nornis.Infrastructure.Persistence.Repositories;
using Nornis.Worker;
using Nornis.Worker.Configuration;
using OpenAI.Chat;

var builder = Host.CreateDefaultBuilder(args)
    .ConfigureServices((context, services) =>
    {
        var configuration = context.Configuration;

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
        services.AddScoped<IReviewBatchRepository, ReviewBatchRepository>();
        services.AddScoped<IReviewProposalRepository, ReviewProposalRepository>();
        services.AddScoped<ISourceReferenceRepository, SourceReferenceRepository>();
        services.AddScoped<IAiUsageRecordRepository, AiUsageRecordRepository>();
        services.AddScoped<IArtifactRepository, ArtifactRepository>();
        services.AddScoped<IArtifactFactRepository, ArtifactFactRepository>();

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

        // Daily AI budget guard (checked before every extraction AI call)
        services.Configure<AiBudgetOptions>(configuration.GetSection(AiBudgetOptions.SectionName));
        services.AddScoped<IAiBudgetGuard, AiBudgetGuard>();

        // Extraction service
        services.AddScoped<IExtractionService, ExtractionService>();

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

        // Hosted service
        services.AddHostedService<ExtractionWorker>();
    });

var host = builder.Build();
host.Run();
