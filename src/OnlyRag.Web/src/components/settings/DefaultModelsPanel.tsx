import {
  AdjustableModelContextBar,
  UnifiedPresetBar,
  normalizeOptionalValue,
  type UnifiedPresetLevel
} from "./SettingsSection.helpers";
import { useSettingsSectionContext } from "./SettingsSectionContext";

export function DefaultModelsPanel() {
  const {
    formState,
    setFormState,
    models,
    chatModelDetailsLoading,
    chatModelDetails,
    chatNumCtxRecommendation,
    embeddingModelDetailsLoading,
    embeddingModelDetails,
    embeddingRecommendations,
    translationModelDetailsLoading,
    translationModelDetails,
    translationNumCtxRecommendation,
    codingModelDetailsLoading,
    codingModelDetails,
    codingNumCtxRecommendation,
    unavailableDefaults,
    hasDirtyOllamaSettings,
    saveSettings,
    isBusy
  } = useSettingsSectionContext();

  const ctxVal = formState.chatNumCtx ?? formState.codingNumCtx;
  const activePreset: UnifiedPresetLevel =
    ctxVal === null ? "auto" :
    ctxVal <= 4096 ? "basso" :
    ctxVal <= 8192 ? "medio" : "alto";

  function handleSelectPreset(preset: UnifiedPresetLevel) {
    if (preset === "basso") {
      setFormState((curr) => ({ ...curr, chatNumCtx: 4096, translationNumCtx: 4096, codingNumCtx: 4096 }));
    } else if (preset === "medio") {
      setFormState((curr) => ({ ...curr, chatNumCtx: 8192, translationNumCtx: 8192, codingNumCtx: 8192 }));
    } else if (preset === "alto") {
      setFormState((curr) => ({ ...curr, chatNumCtx: 16384, translationNumCtx: 16384, codingNumCtx: 16384 }));
    } else if (preset === "auto") {
      setFormState((curr) => ({ ...curr, chatNumCtx: null, embeddingNumCtx: null, translationNumCtx: null, codingNumCtx: null }));
    }
  }

  return (
        <div className="settings-card settings-card--wide">
          <div className="settings-card__header">
            <h3>Modelli &amp; Finestra Contesto</h3>
          </div>
          <div className="settings-form">
            <UnifiedPresetBar
              title="Preset Finestra Contesto (num_ctx)"
              subtitle="Imposta rapidamente il limite token per Chat, Traduzione e Coding."
              allowedPresets={["basso", "medio", "alto", "auto"]}
              activePreset={activePreset}
              onSelectPreset={handleSelectPreset}
            />
            <label className="field-group" htmlFor="default-chat-model">
              <span>Chat</span>
              <select
                id="default-chat-model"
                value={formState.defaultChatModel ?? ""}
                onChange={(event) =>
                  setFormState((current) => ({
                    ...current,
                    defaultChatModel: normalizeOptionalValue(event.target.value)
                  }))
                }
              >
                <option value="">Nessun modello selezionato</option>
                {models.map((model) => (
                  <option key={model.name} value={model.name}>
                    {model.name}
                  </option>
                ))}
              </select>
            </label>
            {formState.defaultChatModel && (
              <AdjustableModelContextBar
                title="Contesto chat (num_ctx)"
                sliderLabel="num_ctx chat"
                loading={chatModelDetailsLoading}
                details={chatModelDetails}
                fallbackText="Dettagli chat non disponibili."
                value={formState.chatNumCtx}
                recommendedValue={chatNumCtxRecommendation}
                onAutoChange={(isAutomatic) =>
                  setFormState((current) => ({
                    ...current,
                    chatNumCtx: isAutomatic ? null : chatNumCtxRecommendation ?? 2048
                  }))
                }
                onValueChange={(value) =>
                  setFormState((current) => ({ ...current, chatNumCtx: value }))
                }
              />
            )}
            <label className="field-group" htmlFor="default-embedding-model">
              <span>Embeddings</span>
              <select
                id="default-embedding-model"
                value={formState.defaultEmbeddingModel ?? ""}
                onChange={(event) =>
                  setFormState((current) => ({
                    ...current,
                    defaultEmbeddingModel: normalizeOptionalValue(event.target.value)
                  }))
                }
              >
                <option value="">Nessun modello selezionato</option>
                {models.map((model) => (
                  <option key={model.name} value={model.name}>
                    {model.name}
                  </option>
                ))}
              </select>
            </label>

            {formState.defaultEmbeddingModel && (
              <AdjustableModelContextBar
                title="Contesto embedding (num_ctx)"
                sliderLabel="num_ctx embedding"
                loading={embeddingModelDetailsLoading}
                details={embeddingModelDetails}
                fallbackText="Dettagli embedding non disponibili."
                value={formState.embeddingNumCtx}
                recommendedValue={embeddingRecommendations?.embeddingNumCtx ?? null}
                onAutoChange={(isAutomatic) =>
                  setFormState((current) => ({
                    ...current,
                    embeddingNumCtx: isAutomatic ? null : embeddingRecommendations?.embeddingNumCtx ?? 2048
                  }))
                }
                onValueChange={(value) =>
                  setFormState((current) => ({ ...current, embeddingNumCtx: value }))
                }
              />
            )}
            <label className="field-group" htmlFor="default-translation-model">
              <span>Traduzione</span>
              <select
                id="default-translation-model"
                value={formState.defaultTranslationModel ?? ""}
                onChange={(event) =>
                  setFormState((current) => ({
                    ...current,
                    defaultTranslationModel: normalizeOptionalValue(event.target.value)
                  }))
                }
              >
                <option value="">Nessun modello selezionato</option>
                {models.map((model) => (
                  <option key={model.name} value={model.name}>
                    {model.name}
                  </option>
                ))}
              </select>
            </label>
            {formState.defaultTranslationModel && (
              <AdjustableModelContextBar
                title="Contesto traduzione (num_ctx)"
                sliderLabel="num_ctx traduzione"
                loading={translationModelDetailsLoading}
                details={translationModelDetails}
                fallbackText="Dettagli traduzione non disponibili."
                value={formState.translationNumCtx}
                recommendedValue={translationNumCtxRecommendation}
                onAutoChange={(isAutomatic) =>
                  setFormState((current) => ({
                    ...current,
                    translationNumCtx: isAutomatic ? null : translationNumCtxRecommendation ?? 2048
                  }))
                }
                onValueChange={(value) =>
                  setFormState((current) => ({ ...current, translationNumCtx: value }))
                }
              />
            )}
            <label className="field-group" htmlFor="default-coding-model">
              <span>Coding</span>
              <select
                id="default-coding-model"
                value={formState.defaultCodingModel ?? ""}
                onChange={(event) =>
                  setFormState((current) => ({
                    ...current,
                    defaultCodingModel: normalizeOptionalValue(event.target.value)
                  }))
                }
              >
                <option value="">Nessun modello selezionato</option>
                {models.map((model) => (
                  <option key={model.name} value={model.name}>
                    {model.name}
                  </option>
                ))}
              </select>
            </label>
            {formState.defaultCodingModel && (
              <AdjustableModelContextBar
                title="Contesto coding (num_ctx)"
                sliderLabel="num_ctx coding"
                loading={codingModelDetailsLoading}
                details={codingModelDetails}
                fallbackText="Dettagli coding non disponibili."
                value={formState.codingNumCtx}
                recommendedValue={codingNumCtxRecommendation}
                onAutoChange={(isAutomatic) =>
                  setFormState((current) => ({
                    ...current,
                    codingNumCtx: isAutomatic ? null : codingNumCtxRecommendation ?? 2048
                  }))
                }
                onValueChange={(value) =>
                  setFormState((current) => ({ ...current, codingNumCtx: value }))
                }
              />
            )}
            {unavailableDefaults.length > 0 && (
              <div className="panel-note panel-note--warning" role="alert">
                <p>Alcuni modelli salvati non sono piu presenti in Ollama: {unavailableDefaults.join(", ")}.</p>
              </div>
            )}
            <div
              className={hasDirtyOllamaSettings
                ? "settings-actions settings-actions--dirty"
                : "settings-actions"}
              aria-live="polite"
            >
              <button type="button" onClick={saveSettings} disabled={isBusy || !hasDirtyOllamaSettings}>
                Salva modelli predefiniti
              </button>
              {hasDirtyOllamaSettings && <span className="dirty-hint">Modifiche non salvate</span>}
            </div>
          </div>
        </div>
  );
}

