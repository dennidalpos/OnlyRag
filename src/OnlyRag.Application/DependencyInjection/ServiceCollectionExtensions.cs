using Microsoft.Extensions.DependencyInjection;
using OnlyRag.Application.Documents;
using OnlyRag.Application.Jobs;
using OnlyRag.Application.Translations;

namespace OnlyRag.Application.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddOnlyRagApplicationServices(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<RunningJobCancellationRegistry>();
        services.AddSingleton<JobApplicationService>();
        services.AddSingleton<DocumentPipelineApplicationService>();
        services.AddSingleton<TranslationApplicationService>();

        return services;
    }
}
