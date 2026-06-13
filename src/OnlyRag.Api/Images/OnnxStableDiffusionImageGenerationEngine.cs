using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.ML.OnnxRuntime;
using OnnxStack.Core.Model;
using OnnxStack.StableDiffusion.Config;
using OnnxStack.StableDiffusion.Enums;
using OnnxStack.StableDiffusion.Pipelines;
using OnlyRag.Core;

namespace OnlyRag.Api.Images;

internal sealed class OnnxStableDiffusionImageGenerationEngine : IImageGenerationEngine
{
    private const string DirectMlProvider = "DirectML";
    private const string CpuProvider = "CPU";

    private readonly object gate = new();
    private string activeExecutionProvider = DirectMlProvider;
    private string? fallbackReason;
    private bool isInitialized;

    public ImageGenerationEngineStatus GetStatus()
    {
        lock (gate)
        {
            return new ImageGenerationEngineStatus(activeExecutionProvider, fallbackReason, isInitialized);
        }
    }

    public async Task<ImageGenerationEngineResult> GenerateAsync(
        ImageGenerationRequest request,
        string modelDirectory,
        bool preferGpu,
        CancellationToken cancellationToken = default)
    {
        if (preferGpu)
        {
            try
            {
                ImageGenerationEngineResult directMl = await GenerateWithProviderAsync(
                    request,
                    modelDirectory,
                    CreateDirectMlProvider(),
                    DirectMlProvider,
                    fallbackReason: null,
                    cancellationToken);
                SetStatus(directMl.ActiveExecutionProvider, directMl.FallbackReason);
                return directMl;
            }
            catch (Exception ex) when (IsRecoverableProviderException(ex))
            {
                string reason = $"DirectML non disponibile per questo modello o dispositivo: {ex.Message}";
                ImageGenerationEngineResult cpu = await GenerateWithProviderAsync(
                    request,
                    modelDirectory,
                    CreateCpuProvider(),
                    CpuProvider,
                    reason,
                    cancellationToken);
                SetStatus(cpu.ActiveExecutionProvider, cpu.FallbackReason);
                return cpu;
            }
        }

        ImageGenerationEngineResult result = await GenerateWithProviderAsync(
            request,
            modelDirectory,
            CreateCpuProvider(),
            CpuProvider,
            fallbackReason: null,
            cancellationToken);
        SetStatus(result.ActiveExecutionProvider, result.FallbackReason);
        return result;
    }

    private static async Task<ImageGenerationEngineResult> GenerateWithProviderAsync(
        ImageGenerationRequest request,
        string modelDirectory,
        OnnxExecutionProvider provider,
        string providerName,
        string? fallbackReason,
        CancellationToken cancellationToken)
    {
        StableDiffusionXLPipeline pipeline = StableDiffusionXLPipeline.CreatePipeline(
            provider,
            modelDirectory,
            ModelType.Turbo,
            NullLogger.Instance);

        try
        {
            List<ImageGenerationBinary> images = [];
            for (int index = 0; index < request.BatchSize; index++)
            {
                long seed = request.Seed is null
                    ? Random.Shared.NextInt64(0, int.MaxValue)
                    : request.Seed.Value + index;
                GenerateOptions options = CreateGenerateOptions(request, seed);
                await using MemoryStream stream = new();
                using OnnxStack.Core.Image.OnnxImage image = await pipeline.GenerateAsync(
                    options,
                    null,
                    cancellationToken);
                await image.SaveAsync(stream);
                images.Add(new ImageGenerationBinary(stream.ToArray(), "image/png", ".png"));
            }

            return new ImageGenerationEngineResult(images, providerName, fallbackReason);
        }
        finally
        {
            await pipeline.UnloadAsync();
        }
    }

    private static GenerateOptions CreateGenerateOptions(ImageGenerationRequest request, long seed)
    {
        return new GenerateOptions
        {
            Diffuser = DiffuserType.TextToImage,
            Prompt = request.Prompt,
            NegativePrompt = request.NegativePrompt ?? string.Empty,
            SchedulerOptions = new SchedulerOptions
            {
                SchedulerType = SchedulerType.EulerAncestral,
                Width = request.Width,
                Height = request.Height,
                Seed = unchecked((int)Math.Clamp(seed, 0, int.MaxValue)),
                InferenceSteps = request.Steps,
                GuidanceScale = 0,
                TimestepSpacing = TimestepSpacingType.Trailing
            },
            IsLowMemoryComputeEnabled = true,
            IsLowMemoryDecoderEnabled = true,
            IsLowMemoryEncoderEnabled = true,
            IsLowMemoryTextEncoderEnabled = true
        };
    }

    private static OnnxExecutionProvider CreateDirectMlProvider()
    {
        return new OnnxExecutionProvider(DirectMlProvider, _ =>
        {
            SessionOptions options = CreateBaseSessionOptions();
            options.AppendExecutionProvider_DML(0);
            return options;
        });
    }

    private static OnnxExecutionProvider CreateCpuProvider()
    {
        return new OnnxExecutionProvider(CpuProvider, _ =>
        {
            SessionOptions options = CreateBaseSessionOptions();
            options.AppendExecutionProvider_CPU(0);
            return options;
        });
    }

    private static SessionOptions CreateBaseSessionOptions()
    {
        return new SessionOptions
        {
            EnableMemoryPattern = false,
            ExecutionMode = ExecutionMode.ORT_SEQUENTIAL,
            GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL
        };
    }

    private void SetStatus(string provider, string? reason)
    {
        lock (gate)
        {
            activeExecutionProvider = provider;
            fallbackReason = reason;
            isInitialized = true;
        }
    }

    private static bool IsRecoverableProviderException(Exception exception)
    {
        return exception is OnnxRuntimeException
            or DllNotFoundException
            or EntryPointNotFoundException
            or InvalidOperationException
            or NotSupportedException
            or IOException and not FileNotFoundException and not DirectoryNotFoundException;
    }
}
