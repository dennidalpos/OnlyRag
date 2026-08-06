using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using System.Text;
using System.Text.Json;

namespace OnlyRag.Infrastructure.Retrieval;

public sealed class OnnxCrossEncoderReRankerService : IReRankerService, IDisposable
{
    private readonly RerankerModelManager modelManager;
    private readonly IReRankerService fallbackReRanker;
    private readonly object sessionLock = new();

    private InferenceSession? session;
    private XlmRobertaTokenizer? vocab;
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

        (InferenceSession? currentSession, XlmRobertaTokenizer? currentVocab) = GetOrInitialize();
        if (currentSession is null || currentVocab is null)
        {
            return await fallbackReRanker.ReRankAsync(query, candidates, cancellationToken);
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            double[] scores = ComputeCrossScoresBatched(currentSession, currentVocab, query, candidates);

            List<ReRankResult> results = new(candidates.Count);
            for (int i = 0; i < candidates.Count; i++)
            {
                results.Add(new ReRankResult(candidates[i].ChunkId, Math.Round(scores[i], 4)));
            }

            return results
                .OrderByDescending(r => r.Score)
                .ToList();
        }
        catch (Exception)
        {
            // Graceful fallback on ONNX runtime error.
            return await fallbackReRanker.ReRankAsync(query, candidates, cancellationToken);
        }
    }

    public Task WarmupAsync(CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            (InferenceSession? currentSession, XlmRobertaTokenizer? currentVocab) = GetOrInitialize();
            if (currentSession != null && currentVocab != null)
            {
                try
                {
                    ComputeCrossScoresBatched(currentSession, currentVocab, "warmup query", [new ReRankCandidate(0, "warmup content candidate")]);
                }
                catch
                {
                    // Ignore dry-run warmup exception
                }
            }
        }, cancellationToken);
    }

    private (InferenceSession?, XlmRobertaTokenizer?) GetOrInitialize()
    {
        lock (sessionLock)
        {
            if (sessionFailed)
            {
                return (null, null);
            }

            if (isInitialized)
            {
                return (session, vocab);
            }

            string modelPath = modelManager.GetDefaultModelPath();
            string vocabPath = modelManager.GetVocabPath();
            if (!File.Exists(modelPath) || !File.Exists(vocabPath))
            {
                return (null, null);
            }

            try
            {
                vocab = XlmRobertaTokenizer.Load(vocabPath);

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
                return (session, vocab);
            }
            catch
            {
                sessionFailed = true;
                session?.Dispose();
                session = null;
                vocab = null;
                return (null, null);
            }
        }
    }

    private static double[] ComputeCrossScoresBatched(
        InferenceSession currentSession,
        XlmRobertaTokenizer currentVocab,
        string query,
        IReadOnlyList<ReRankCandidate> candidates)
    {
        int batchSize = candidates.Count;
        if (batchSize == 0)
        {
            return [];
        }

        const int maxSeqLength = 512;
        long[] inputIds = new long[batchSize * maxSeqLength];
        long[] attentionMask = new long[batchSize * maxSeqLength];
        Array.Fill(inputIds, currentVocab.PadId);

        for (int i = 0; i < batchSize; i++)
        {
            int offset = i * maxSeqLength;
            string content = candidates[i].Content;

            List<int> queryTokenIds = currentVocab.Tokenize(query, maxSeqLength / 2 - 3);
            int remaining = maxSeqLength - queryTokenIds.Count - 4;
            List<int> contentTokenIds = currentVocab.Tokenize(content, Math.Max(1, remaining));

            int pos = 0;
            inputIds[offset + pos] = currentVocab.BosId;
            attentionMask[offset + pos] = 1;
            pos++;

            foreach (int id in queryTokenIds)
            {
                inputIds[offset + pos] = id;
                attentionMask[offset + pos] = 1;
                pos++;
            }

            inputIds[offset + pos] = currentVocab.EosId;
            attentionMask[offset + pos] = 1;
            pos++;

            inputIds[offset + pos] = currentVocab.EosId;
            attentionMask[offset + pos] = 1;
            pos++;

            foreach (int id in contentTokenIds)
            {
                inputIds[offset + pos] = id;
                attentionMask[offset + pos] = 1;
                pos++;
            }

            inputIds[offset + pos] = currentVocab.EosId;
            attentionMask[offset + pos] = 1;
        }

        int[] shape = [batchSize, maxSeqLength];
        DenseTensor<long> inputIdsTensor = new(inputIds, shape);
        DenseTensor<long> attentionMaskTensor = new(attentionMask, shape);
        List<NamedOnnxValue> container =
        [
            NamedOnnxValue.CreateFromTensor("input_ids", inputIdsTensor),
            NamedOnnxValue.CreateFromTensor("attention_mask", attentionMaskTensor)
        ];

        using IDisposableReadOnlyCollection<DisposableNamedOnnxValue> results = currentSession.Run(container);
        DisposableNamedOnnxValue? outputValue = results.Count > 0 ? results[0] : null;
        if (outputValue is null)
        {
            return candidates.Select(_ => 0.5d).ToArray();
        }

        Tensor<float> logits = outputValue.AsTensor<float>();
        double[] scores = new double[batchSize];
        for (int i = 0; i < batchSize; i++)
        {
            float rawScore = logits.Dimensions.Length > 1 ? logits[i, 0] : logits[i];
            scores[i] = Sigmoid(rawScore);
        }

        return scores;
    }

    private static double Sigmoid(float x) => 1.0d / (1.0d + Math.Exp(-x));

    public void Dispose()
    {
        lock (sessionLock)
        {
            session?.Dispose();
            session = null;
            vocab = null;
            isInitialized = false;
        }
    }

    /// <summary>
    /// Minimal XLM-RoBERTa SentencePiece-Unigram tokenizer backed by the model's
    /// official Hugging Face tokenizer.json. It keeps the reranker self-contained.
    /// </summary>
    private sealed class XlmRobertaTokenizer
    {
        private const char SpaceMarker = '▁';
        private readonly Dictionary<string, Token> tokens;
        private readonly int maxTokenLength;
        public int BosId { get; }
        public int EosId { get; }
        public int PadId { get; }
        public int UnkId { get; }

        private XlmRobertaTokenizer(Dictionary<string, Token> tokens, int maxTokenLength)
        {
            this.tokens = tokens;
            this.maxTokenLength = maxTokenLength;
            BosId = tokens.GetValueOrDefault("<s>").Id;
            EosId = tokens.GetValueOrDefault("</s>").Id;
            PadId = tokens.GetValueOrDefault("<pad>").Id;
            UnkId = tokens.GetValueOrDefault("<unk>").Id;
        }

        public static XlmRobertaTokenizer Load(string tokenizerPath)
        {
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(tokenizerPath));
            JsonElement vocab = document.RootElement.GetProperty("model").GetProperty("vocab");
            Dictionary<string, Token> result = new(vocab.GetArrayLength(), StringComparer.Ordinal);
            int maxLength = 1;
            int index = 0;
            foreach (JsonElement item in vocab.EnumerateArray())
            {
                string text = item[0].GetString() ?? string.Empty;
                result[text] = new Token(index++, item[1].GetDouble());
                maxLength = Math.Max(maxLength, text.Length);
            }
            return new XlmRobertaTokenizer(result, maxLength);
        }

        public List<int> Tokenize(string text, int maxTokens)
        {
            string normalized = SpaceMarker + string.Join(SpaceMarker, text.Normalize(NormalizationForm.FormKC)
                .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
            int length = normalized.Length;
            double[] bestScores = Enumerable.Repeat(double.NegativeInfinity, length + 1).ToArray();
            int[] previous = new int[length + 1];
            int[] ids = new int[length + 1];
            bestScores[0] = 0;

            for (int start = 0; start < length; start++)
            {
                if (double.IsNegativeInfinity(bestScores[start])) continue;
                bool matched = false;
                int upperBound = Math.Min(length, start + maxTokenLength);
                for (int end = start + 1; end <= upperBound; end++)
                {
                    if (!tokens.TryGetValue(normalized[start..end], out Token token)) continue;
                    matched = true;
                    double score = bestScores[start] + token.Score;
                    if (score > bestScores[end]) { bestScores[end] = score; previous[end] = start; ids[end] = token.Id; }
                }
                if (!matched && start + 1 <= length && bestScores[start] - 10 > bestScores[start + 1])
                {
                    bestScores[start + 1] = bestScores[start] - 10;
                    previous[start + 1] = start;
                    ids[start + 1] = UnkId;
                }
            }

            List<int> result = [];
            for (int position = length; position > 0; position = previous[position]) result.Add(ids[position]);
            result.Reverse();
            return result.Take(maxTokens).ToList();
        }

        private readonly record struct Token(int Id, double Score);
    }
}
