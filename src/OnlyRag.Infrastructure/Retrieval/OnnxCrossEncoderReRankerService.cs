using System.Text.RegularExpressions;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace OnlyRag.Infrastructure.Retrieval;

public sealed class OnnxCrossEncoderReRankerService : IReRankerService, IDisposable
{
    private static readonly Regex WordSplitter = new(@"\w+", RegexOptions.Compiled);
    private readonly RerankerModelManager modelManager;
    private readonly IReRankerService fallbackReRanker;
    private readonly object sessionLock = new();

    private InferenceSession? session;
    private bool isInitialized;
    private bool sessionFailed;

    public OnnxCrossEncoderReRankerService(
        RerankerModelManager modelManager,
        HeuristicReRankerService? fallbackReRanker = null)
    {
        this.modelManager = modelManager;
        this.fallbackReRanker = fallbackReRanker ?? new HeuristicReRankerService();
    }

    public async Task<IReadOnlyList<ReRankResult>> ReRankAsync(
        string query,
        IReadOnlyList<ReRankCandidate> candidates,
        CancellationToken cancellationToken = default)
    {
        if (candidates.Count == 0 || string.IsNullOrWhiteSpace(query))
        {
            return [];
        }

        InferenceSession? currentSession = GetOrInitializeSession();
        if (currentSession is null)
        {
            return await fallbackReRanker.ReRankAsync(query, candidates, cancellationToken);
        }

        try
        {
            List<ReRankResult> results = [];
            foreach (ReRankCandidate candidate in candidates)
            {
                cancellationToken.ThrowIfCancellationRequested();
                double score = ComputeOnnxCrossScore(currentSession, query, candidate.Content);
                results.Add(new ReRankResult(candidate.ChunkId, Math.Round(score, 4)));
            }

            return results
                .OrderByDescending(r => r.Score)
                .ToList();
        }
        catch (Exception)
        {
            // Graceful fallback on ONNX runtime error
            return await fallbackReRanker.ReRankAsync(query, candidates, cancellationToken);
        }
    }

    private InferenceSession? GetOrInitializeSession()
    {
        lock (sessionLock)
        {
            if (sessionFailed)
            {
                return null;
            }

            if (isInitialized)
            {
                return session;
            }

            string modelPath = modelManager.GetDefaultModelPath();
            if (!File.Exists(modelPath))
            {
                return null;
            }

            try
            {
                SessionOptions options = new();
                try
                {
                    options.AppendExecutionProvider_DML(0);
                }
                catch
                {
                    options.AppendExecutionProvider_CPU();
                }

                session = new InferenceSession(modelPath, options);
                isInitialized = true;
                return session;
            }
            catch
            {
                sessionFailed = true;
                session?.Dispose();
                session = null;
                return null;
            }
        }
    }

    private static double ComputeOnnxCrossScore(InferenceSession currentSession, string query, string content)
    {
        int maxSeqLength = 512;
        long[] inputIds = TokenizePair(query, content, maxSeqLength, out long[] attentionMask, out long[] tokenTypeIds);

        int[] shape = [1, maxSeqLength];
        DenseTensor<long> inputIdsTensor = new(inputIds, shape);
        DenseTensor<long> attentionMaskTensor = new(attentionMask, shape);
        DenseTensor<long> tokenTypeIdsTensor = new(tokenTypeIds, shape);

        List<NamedOnnxValue> container = new()
        {
            NamedOnnxValue.CreateFromTensor("input_ids", inputIdsTensor),
            NamedOnnxValue.CreateFromTensor("attention_mask", attentionMaskTensor),
            NamedOnnxValue.CreateFromTensor("token_type_ids", tokenTypeIdsTensor)
        };

        using IDisposableReadOnlyCollection<DisposableNamedOnnxValue> results = currentSession.Run(container);
        DisposableNamedOnnxValue? outputValue = results.Count > 0 ? results[0] : null;
        if (outputValue is null)
        {
            return 0.5d;
        }

        Tensor<float> logits = outputValue.AsTensor<float>();
        float rawScore = logits.GetValue(0);
        return Sigmoid(rawScore);
    }

    private static long[] TokenizePair(
        string textA,
        string textB,
        int maxLen,
        out long[] attentionMask,
        out long[] tokenTypeIds)
    {
        long clsToken = 101;
        long sepToken = 102;
        long padToken = 0;

        List<long> tokensA = SimpleWordPieceTokenize(textA);
        List<long> tokensB = SimpleWordPieceTokenize(textB);

        int maxTokensA = Math.Min(tokensA.Count, maxLen / 2 - 2);
        int maxTokensB = Math.Min(tokensB.Count, maxLen - maxTokensA - 3);

        List<long> fullTokens = new() { clsToken };
        fullTokens.AddRange(tokensA.Take(maxTokensA));
        fullTokens.Add(sepToken);

        int sepIndexA = fullTokens.Count;

        fullTokens.AddRange(tokensB.Take(maxTokensB));
        fullTokens.Add(sepToken);

        long[] ids = new long[maxLen];
        attentionMask = new long[maxLen];
        tokenTypeIds = new long[maxLen];

        for (int i = 0; i < maxLen; i++)
        {
            if (i < fullTokens.Count)
            {
                ids[i] = fullTokens[i];
                attentionMask[i] = 1;
                tokenTypeIds[i] = i >= sepIndexA ? 1 : 0;
            }
            else
            {
                ids[i] = padToken;
                attentionMask[i] = 0;
                tokenTypeIds[i] = 0;
            }
        }

        return ids;
    }

    private static List<long> SimpleWordPieceTokenize(string text)
    {
        List<long> tokens = new();
        if (string.IsNullOrWhiteSpace(text))
        {
            return tokens;
        }

        MatchCollection matches = WordSplitter.Matches(text.ToLowerInvariant());
        foreach (Match match in matches)
        {
            string word = match.Value;
            long tokenHash = Math.Abs(word.GetHashCode(StringComparison.Ordinal)) % 29000 + 1000;
            tokens.Add(tokenHash);
        }

        return tokens;
    }

    private static double Sigmoid(float x) => 1.0d / (1.0d + Math.Exp(-x));

    public void Dispose()
    {
        lock (sessionLock)
        {
            session?.Dispose();
            session = null;
            isInitialized = false;
        }
    }
}
