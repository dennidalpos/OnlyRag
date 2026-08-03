import { useState, useRef, useEffect } from "react";
import { ChevronDown, Cpu, Check, Sparkles } from "lucide-react";
import type { OllamaModel } from "../../api";

type ModelSelectorDropdownProps = {
  models: OllamaModel[];
  selectedModel: string | null;
  onSelectModel: (modelName: string) => void;
  label?: string;
  placeholder?: string;
  disabled?: boolean;
};

export function ModelSelectorDropdown({
  models,
  selectedModel,
  onSelectModel,
  label = "Modello LLM",
  placeholder = "Seleziona un modello...",
  disabled = false
}: ModelSelectorDropdownProps) {
  const [isOpen, setIsOpen] = useState(false);
  const containerRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    function handleClickOutside(event: MouseEvent) {
      if (containerRef.current && !containerRef.current.contains(event.target as Node)) {
        setIsOpen(false);
      }
    }
    document.addEventListener("mousedown", handleClickOutside);
    return () => document.removeEventListener("mousedown", handleClickOutside);
  }, []);

  const activeModel = models.find((m) => m.name === selectedModel);

  return (
    <div className="model-selector" ref={containerRef}>
      {label && <label className="sr-only">{label}</label>}
      <button
        type="button"
        className="model-selector-trigger"
        onClick={() => !disabled && setIsOpen((prev) => !prev)}
        disabled={disabled}
        aria-expanded={isOpen}
      >
        <Sparkles size={14} className="text-accent" />
        <span className="truncate max-w-[160px]">
          {activeModel ? activeModel.name : selectedModel || placeholder}
        </span>
        <ChevronDown size={14} className="text-muted" />
      </button>

      {isOpen && (
        <div className="model-selector-popover" role="listbox">
          {models.length === 0 ? (
            <div className="p-3 text-sm text-muted text-center">Nessun modello disponibile</div>
          ) : (
            models.map((model) => {
              const isSelected = model.name === selectedModel;
              const paramSize = model.parameterSize || "";
              const quant = model.quantizationLevel || "";
              return (
                <button
                  key={model.name}
                  type="button"
                  role="option"
                  aria-selected={isSelected}
                  className={`model-selector-option ${isSelected ? "model-selector-option--selected" : ""}`}
                  onClick={() => {
                    onSelectModel(model.name);
                    setIsOpen(false);
                  }}
                >
                  <div className="model-selector-option__header">
                    <span className="model-selector-option__name">{model.name}</span>
                    {isSelected && <Check size={14} className="text-primary" />}
                  </div>
                  {(paramSize || quant) && (
                    <div className="model-selector-option__details">
                      <Cpu size={12} />
                      {paramSize && <span>{paramSize}</span>}
                      {paramSize && quant && <span>•</span>}
                      {quant && <span>{quant}</span>}
                    </div>
                  )}
                </button>
              );
            })
          )}
        </div>
      )}
    </div>
  );
}
