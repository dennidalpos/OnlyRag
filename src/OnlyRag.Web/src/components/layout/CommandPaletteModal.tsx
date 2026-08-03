import { useEffect, useRef, useState } from "react";
import { Search, MessageSquare, Code, FileText, Languages, Image, Settings, Sparkles } from "lucide-react";
import type { SectionId } from "./Sidebar";
import { useModalFocusTrap } from "../common/useModalFocusTrap";

type CommandPaletteModalProps = {
  isOpen: boolean;
  onClose: () => void;
  onSelectSection: (section: SectionId) => void;
};

type CommandItem = {
  id: string;
  label: string;
  group: "Sezioni" | "Azioni Rapide";
  sectionId?: SectionId;
  shortcut?: string;
  icon: React.ReactNode;
};

const commands: CommandItem[] = [
  { id: "nav-chat", label: "Apri Chat & RAG Workspace", group: "Sezioni", sectionId: "chat", shortcut: "Ctrl+1", icon: <MessageSquare size={16} /> },
  { id: "nav-coding", label: "Apri Coding & Subagent Studio", group: "Sezioni", sectionId: "coding", shortcut: "Ctrl+2", icon: <Code size={16} /> },
  { id: "nav-documents", label: "Apri Gestione Documenti", group: "Sezioni", sectionId: "documents", shortcut: "Ctrl+3", icon: <FileText size={16} /> },
  { id: "nav-translation", label: "Apri Traduttore Multilingua", group: "Sezioni", sectionId: "translation", shortcut: "Ctrl+4", icon: <Languages size={16} /> },
  { id: "nav-images", label: "Apri Generazione Immagini DirectML", group: "Sezioni", sectionId: "images", shortcut: "Ctrl+5", icon: <Image size={16} /> },
  { id: "nav-settings", label: "Apri Impostazioni di Sistema", group: "Sezioni", sectionId: "settings", shortcut: "Ctrl+6", icon: <Settings size={16} /> },
  { id: "act-new-chat", label: "Nuova Conversazione Chat", group: "Azioni Rapide", sectionId: "chat", icon: <Sparkles size={16} /> }
];

export function CommandPaletteModal({ isOpen, onClose, onSelectSection }: CommandPaletteModalProps) {
  const [query, setQuery] = useState("");
  const [selectedIndex, setSelectedIndex] = useState(0);
  const modalRef = useRef<HTMLDivElement>(null);
  useModalFocusTrap(modalRef, isOpen, { onEscape: onClose });

  useEffect(() => {
    if (isOpen) {
      setQuery("");
      setSelectedIndex(0);
    }
  }, [isOpen]);

  if (!isOpen) return null;

  const filteredCommands = commands.filter((cmd) =>
    cmd.label.toLowerCase().includes(query.toLowerCase())
  );

  function handleSelect(cmd: CommandItem) {
    if (cmd.sectionId) {
      onSelectSection(cmd.sectionId);
    }
    onClose();
  }

  function handleKeyDown(e: React.KeyboardEvent) {
    if (e.key === "ArrowDown") {
      e.preventDefault();
      setSelectedIndex((prev) => (prev + 1) % Math.max(1, filteredCommands.length));
    } else if (e.key === "ArrowUp") {
      e.preventDefault();
      setSelectedIndex((prev) => (prev - 1 + filteredCommands.length) % Math.max(1, filteredCommands.length));
    } else if (e.key === "Enter" && filteredCommands[selectedIndex]) {
      e.preventDefault();
      handleSelect(filteredCommands[selectedIndex]);
    } else if (e.key === "Escape") {
      onClose();
    }
  }

  return (
    <div className="command-palette-backdrop" onClick={onClose} aria-modal="true" role="dialog">
      <div
        ref={modalRef}
        className="command-palette-modal"
        onClick={(e) => e.stopPropagation()}
        onKeyDown={handleKeyDown}
      >
        <div className="command-palette-search">
          <Search size={18} className="text-muted" />
          <input
            type="text"
            className="command-palette-input"
            placeholder="Cerca comando, sezione o modello... (Esc per chiudere)"
            value={query}
            onChange={(e) => {
              setQuery(e.target.value);
              setSelectedIndex(0);
            }}
            autoFocus
          />
        </div>
        <div className="command-palette-results">
          {filteredCommands.length === 0 ? (
            <div className="p-4 text-center text-muted text-sm">Nessun comando trovato per "{query}"</div>
          ) : (
            filteredCommands.map((cmd, idx) => (
              <button
                key={cmd.id}
                type="button"
                className={`command-palette-item ${idx === selectedIndex ? "command-palette-item--selected" : ""}`}
                onClick={() => handleSelect(cmd)}
                onMouseEnter={() => setSelectedIndex(idx)}
              >
                <span className="command-palette-item__label">
                  {cmd.icon}
                  <span>{cmd.label}</span>
                </span>
                {cmd.shortcut && (
                  <span className="command-palette-item__badge">{cmd.shortcut}</span>
                )}
              </button>
            ))
          )}
        </div>
      </div>
    </div>
  );
}
