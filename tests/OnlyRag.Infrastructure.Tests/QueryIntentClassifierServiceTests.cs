using OnlyRag.Core;
using OnlyRag.Infrastructure.Retrieval;
using Xunit;

namespace OnlyRag.Infrastructure.Tests;

public sealed class QueryIntentClassifierServiceTests
{
    private readonly QueryIntentClassifierService _classifier = new();

    [Theory]
    [InlineData("async Task<int> ProcessQueryAsync(string query, CancellationToken cancellationToken) => await repo.SearchAsync();", QueryIntent.CodeSearch, 0.45f)]
    [InlineData("Come si definisce una class public interface in C# con metodo async return?", QueryIntent.CodeSearch, 0.45f)]
    public void ClassifyIntent_IdentifiesCodeSearchCorrectly(string query, QueryIntent expectedIntent, float expectedMinScore)
    {
        QueryIntentClassificationResult result = _classifier.ClassifyIntent(query);

        Assert.Equal(expectedIntent, result.Intent);
        Assert.Equal(expectedMinScore, result.MinimumRerankScoreThreshold);
        Assert.True(result.Confidence >= 0.5f);
    }

    [Theory]
    [InlineData("Quali sono le istruzioni di installazione, setup api e configurazione dello schema del database?", QueryIntent.TechnicalDocumentation, 0.25f)]
    [InlineData("Guida architetturale per il deployment della pipeline e documentazione dell'endpoint swagger", QueryIntent.TechnicalDocumentation, 0.25f)]
    public void ClassifyIntent_IdentifiesTechnicalDocsCorrectly(string query, QueryIntent expectedIntent, float expectedMinScore)
    {
        QueryIntentClassificationResult result = _classifier.ClassifyIntent(query);

        Assert.Equal(expectedIntent, result.Intent);
        Assert.Equal(expectedMinScore, result.MinimumRerankScoreThreshold);
        Assert.True(result.Confidence >= 0.5f);
    }

    [Fact]
    public void ClassifyIntent_DefaultsToGeneralQAForGenericQueries()
    {
        QueryIntentClassificationResult result = _classifier.ClassifyIntent("Qual e la capitale dell'Italia?");

        Assert.Equal(QueryIntent.GeneralQA, result.Intent);
        Assert.Equal(0.35f, result.MinimumRerankScoreThreshold);
        Assert.Equal(8, result.RecommendedTopK);
    }
}
