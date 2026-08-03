using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace OnlyRag.Infrastructure.Retrieval;

public sealed class OnnxCrossEncoderReRankerService : IReRankerService, IDisposable
{
    private readonly RerankerModelManager modelManager;
    private readonly IReRankerService fallbackReRanker;
    private readonly object sessionLock = new();

    private InferenceSession? session;
    private BertVocab? vocab;
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

        (InferenceSession? currentSession, BertVocab? currentVocab) = GetOrInitialize();
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

    private (InferenceSession?, BertVocab?) GetOrInitialize()
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
                vocab = BertVocab.Load(vocabPath);

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
        BertVocab currentVocab,
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
        long[] tokenTypeIds = new long[batchSize * maxSeqLength];

        for (int i = 0; i < batchSize; i++)
        {
            int offset = i * maxSeqLength;
            string content = candidates[i].Content;

            List<int> queryTokenIds = currentVocab.Tokenize(query, maxSeqLength / 2 - 2);
            int remaining = maxSeqLength - queryTokenIds.Count - 3;
            List<int> contentTokenIds = currentVocab.Tokenize(content, Math.Max(1, remaining));

            int pos = 0;
            inputIds[offset + pos] = currentVocab.ClsId;
            attentionMask[offset + pos] = 1;
            pos++;

            foreach (int id in queryTokenIds)
            {
                inputIds[offset + pos] = id;
                attentionMask[offset + pos] = 1;
                pos++;
            }

            inputIds[offset + pos] = currentVocab.SepId;
            attentionMask[offset + pos] = 1;
            pos++;

            foreach (int id in contentTokenIds)
            {
                inputIds[offset + pos] = id;
                attentionMask[offset + pos] = 1;
                tokenTypeIds[offset + pos] = 1;
                pos++;
            }

            inputIds[offset + pos] = currentVocab.SepId;
            attentionMask[offset + pos] = 1;
            tokenTypeIds[offset + pos] = 1;
        }

        int[] shape = [batchSize, maxSeqLength];
        DenseTensor<long> inputIdsTensor = new(inputIds, shape);
        DenseTensor<long> attentionMaskTensor = new(attentionMask, shape);
        DenseTensor<long> tokenTypeIdsTensor = new(tokenTypeIds, shape);

        List<NamedOnnxValue> container =
        [
            NamedOnnxValue.CreateFromTensor("input_ids", inputIdsTensor),
            NamedOnnxValue.CreateFromTensor("attention_mask", attentionMaskTensor),
            NamedOnnxValue.CreateFromTensor("token_type_ids", tokenTypeIdsTensor)
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
    /// Self-contained BERT WordPiece vocabulary and tokenizer.
    /// Loads vocab.txt (one token per line, line number = token ID) and performs
    /// standard WordPiece sub-word tokenization matching the HuggingFace BERT convention.
    /// </summary>
    private sealed class BertVocab
    {
        private readonly Dictionary<string, int> tokenToId;
        public int ClsId { get; }
        public int SepId { get; }
        public int PadId { get; }
        public int UnkId { get; }

        private BertVocab(Dictionary<string, int> tokenToId)
        {
            this.tokenToId = tokenToId;
            ClsId = tokenToId.GetValueOrDefault("[CLS]", 101);
            SepId = tokenToId.GetValueOrDefault("[SEP]", 102);
            PadId = tokenToId.GetValueOrDefault("[PAD]", 0);
            UnkId = tokenToId.GetValueOrDefault("[UNK]", 100);
        }

        public static BertVocab Load(string vocabPath)
        {
            Dictionary<string, int> dict = new(60_000, StringComparer.Ordinal);
            string[] lines = File.ReadAllLines(vocabPath);
            for (int i = 0; i < lines.Length; i++)
            {
                string token = lines[i].TrimEnd();
                if (token.Length > 0)
                {
                    dict[token] = i;
                }
            }
            return new BertVocab(dict);
        }

        /// <summary>
        /// Performs BERT WordPiece tokenization: lowercase, split on whitespace and
        /// punctuation, then greedily match longest subword pieces from the vocabulary.
        /// </summary>
        public List<int> Tokenize(string text, int maxTokens)
        {
            List<int> result = [];
            if (string.IsNullOrWhiteSpace(text))
            {
                return result;
            }

            string normalized = text.ToLowerInvariant();
            List<string> words = SplitOnWhitespaceAndPunctuation(normalized);

            foreach (string word in words)
            {
                if (result.Count >= maxTokens)
                {
                    break;
                }

                WordPieceTokenizeWord(word, result, maxTokens);
            }

            return result;
        }

        private void WordPieceTokenizeWord(string word, List<int> output, int maxTokens)
        {
            int start = 0;
            while (start < word.Length && output.Count < maxTokens)
            {
                int end = word.Length;
                bool found = false;

                while (start < end)
                {
                    string subword = start == 0
                        ? word[start..end]
                        : $"##{word[start..end]}";

                    if (tokenToId.TryGetValue(subword, out int id))
                    {
                        output.Add(id);
                        start = end;
                        found = true;
                        break;
                    }

                    end--;
                }

                if (!found)
                {
                    // Character not in vocab — emit [UNK] and skip entire word.
                    output.Add(UnkId);
                    break;
                }
            }
        }

        private static List<string> SplitOnWhitespaceAndPunctuation(string text)
        {
            List<string> tokens = [];
            int i = 0;
            while (i < text.Length)
            {
                if (char.IsWhiteSpace(text[i]))
                {
                    i++;
                    continue;
                }

                if (char.IsPunctuation(text[i]) || char.IsSymbol(text[i]))
                {
                    tokens.Add(text[i].ToString());
                    i++;
                    continue;
                }

                int start = i;
                while (i < text.Length && !char.IsWhiteSpace(text[i])
                       && !char.IsPunctuation(text[i]) && !char.IsSymbol(text[i]))
                {
                    i++;
                }
                tokens.Add(text[start..i]);
            }

            return tokens;
        }
    }
}
