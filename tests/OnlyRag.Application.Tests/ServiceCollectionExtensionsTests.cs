using Microsoft.Extensions.DependencyInjection;
using OnlyRag.Application.DependencyInjection;
using OnlyRag.Application.Documents;
using OnlyRag.Application.Jobs;
using OnlyRag.Application.Translations;

namespace OnlyRag.Application.Tests;

public sealed class ServiceCollectionExtensionsTests
{
    [Fact]
    public void AddOnlyRagApplicationServices_registersCoreApplicationServices()
    {
        ServiceCollection services = new();

        services.AddOnlyRagApplicationServices();

        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(RunningJobCancellationRegistry));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(JobApplicationService));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(DocumentPipelineApplicationService));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(TranslationApplicationService));
    }
}
