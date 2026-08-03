import { FileCode, Folder, ChevronRight, ChevronDown, Search, X, ChevronsDown, ChevronsUp } from "lucide-react";
import { useState, useMemo } from "react";

export type FileTreeNode = {
  name: string;
  path: string;
  isDirectory: boolean;
  children?: FileTreeNode[];
};

type WorkspaceFileTreeProps = {
  nodes: FileTreeNode[];
  onSelectFile?: (filePath: string) => void;
  selectedFilePath?: string | null;
};

export function WorkspaceFileTree({ nodes, onSelectFile, selectedFilePath }: WorkspaceFileTreeProps) {
  const [searchQuery, setSearchQuery] = useState("");
  const [expandAllOverride, setExpandAllOverride] = useState<boolean | null>(null);

  const filteredNodes = useMemo(() => {
    if (!searchQuery.trim()) return nodes;
    const query = searchQuery.toLowerCase().trim();

    function filterNode(node: FileTreeNode): FileTreeNode | null {
      const nameMatch = node.name.toLowerCase().includes(query);
      if (!node.isDirectory) {
        return nameMatch ? node : null;
      }
      const filteredChildren = node.children
        ?.map(filterNode)
        .filter((child): child is FileTreeNode => child !== null);

      if (nameMatch || (filteredChildren && filteredChildren.length > 0)) {
        return {
          ...node,
          children: filteredChildren ?? []
        };
      }
      return null;
    }

    return nodes
      .map(filterNode)
      .filter((node): node is FileTreeNode => node !== null);
  }, [nodes, searchQuery]);

  if (nodes.length === 0) {
    return (
      <div className="p-3 text-xs text-muted text-center italic">
        Nessun file nel workspace. Seleziona una cartella di progetto.
      </div>
    );
  }

  return (
    <div className="flex flex-col gap-2 py-1 text-xs">
      {/* Search Bar & Expansion Controls */}
      <div className="flex items-center gap-1.5 px-1">
        <div className="relative flex-1">
          <Search size={13} className="absolute left-2 top-1/2 -translate-y-1/2 text-muted" />
          <input
            type="text"
            placeholder="Cerca file nel workspace..."
            value={searchQuery}
            onChange={(e) => {
              setSearchQuery(e.target.value);
              setExpandAllOverride(null);
            }}
            className="w-full pl-7 pr-6 py-1 bg-card border border-light rounded text-xs text-main focus:outline-none focus:border-focus"
          />
          {searchQuery && (
            <button
              type="button"
              onClick={() => setSearchQuery("")}
              className="absolute right-1.5 top-1/2 -translate-y-1/2 text-muted hover:text-main cursor-pointer"
              title="Cancella ricerca"
            >
              <X size={12} />
            </button>
          )}
        </div>
        <button
          type="button"
          onClick={() => setExpandAllOverride(true)}
          className="p-1 rounded hover:bg-card text-muted hover:text-main cursor-pointer"
          title="Espandi tutte le cartelle"
        >
          <ChevronsDown size={14} />
        </button>
        <button
          type="button"
          onClick={() => setExpandAllOverride(false)}
          className="p-1 rounded hover:bg-card text-muted hover:text-main cursor-pointer"
          title="Comprimi tutte le cartelle"
        >
          <ChevronsUp size={14} />
        </button>
      </div>

      {filteredNodes.length === 0 ? (
        <div className="p-2 text-xs text-muted text-center italic">
          Nessun file corrisponde alla ricerca "{searchQuery}".
        </div>
      ) : (
        <div className="flex flex-col gap-1">
          {filteredNodes.map((node) => (
            <FileTreeNodeItem
              key={node.path}
              node={node}
              onSelectFile={onSelectFile}
              selectedFilePath={selectedFilePath}
              forceExpand={searchQuery.trim().length > 0 ? true : expandAllOverride}
            />
          ))}
        </div>
      )}
    </div>
  );
}

function FileTreeNodeItem({
  node,
  onSelectFile,
  selectedFilePath,
  forceExpand
}: {
  node: FileTreeNode;
  onSelectFile?: (filePath: string) => void;
  selectedFilePath?: string | null;
  forceExpand: boolean | null;
}) {
  const [isOpen, setIsOpen] = useState(true);
  const effectiveIsOpen = forceExpand !== null ? forceExpand : isOpen;
  const isSelected = selectedFilePath === node.path;

  if (node.isDirectory) {
    return (
      <div className="flex flex-col">
        <button
          type="button"
          className="flex items-center gap-1.5 px-2 py-1 rounded hover:bg-card text-main font-semibold cursor-pointer"
          onClick={() => setIsOpen(!effectiveIsOpen)}
        >
          {effectiveIsOpen ? (
            <ChevronDown size={14} className="text-muted" />
          ) : (
            <ChevronRight size={14} className="text-muted" />
          )}
          <Folder size={14} className="text-accent" />
          <span className="truncate">{node.name}</span>
        </button>
        {effectiveIsOpen && node.children && (
          <div className="pl-4 border-l border-light ml-2 flex flex-col gap-0.5 mt-0.5">
            {node.children.map((child) => (
              <FileTreeNodeItem
                key={child.path}
                node={child}
                onSelectFile={onSelectFile}
                selectedFilePath={selectedFilePath}
                forceExpand={forceExpand}
              />
            ))}
          </div>
        )}
      </div>
    );
  }

  return (
    <button
      type="button"
      className={`flex items-center gap-2 px-2 py-1 rounded hover:bg-card cursor-pointer ${
        isSelected ? "bg-primary-light border-focus text-primary font-bold" : "text-muted"
      }`}
      onClick={() => onSelectFile?.(node.path)}
    >
      <FileCode size={14} />
      <span className="truncate">{node.name}</span>
    </button>
  );
}
