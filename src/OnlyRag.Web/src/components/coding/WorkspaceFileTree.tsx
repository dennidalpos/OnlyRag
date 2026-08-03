import { FileCode, Folder, ChevronRight, ChevronDown } from "lucide-react";
import { useState } from "react";

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
  if (nodes.length === 0) {
    return (
      <div className="p-3 text-xs text-muted text-center italic">
        Nessun file nel workspace. Seleziona una cartella di progetto.
      </div>
    );
  }

  return (
    <div className="flex flex-col gap-1 py-1 text-xs">
      {nodes.map((node) => (
        <FileTreeNodeItem
          key={node.path}
          node={node}
          onSelectFile={onSelectFile}
          selectedFilePath={selectedFilePath}
        />
      ))}
    </div>
  );
}

function FileTreeNodeItem({
  node,
  onSelectFile,
  selectedFilePath
}: {
  node: FileTreeNode;
  onSelectFile?: (filePath: string) => void;
  selectedFilePath?: string | null;
}) {
  const [isOpen, setIsOpen] = useState(true);
  const isSelected = selectedFilePath === node.path;

  if (node.isDirectory) {
    return (
      <div className="flex flex-col">
        <button
          type="button"
          className="flex items-center gap-1.5 px-2 py-1 rounded hover:bg-card text-main font-semibold cursor-pointer"
          onClick={() => setIsOpen((prev) => !prev)}
        >
          {isOpen ? <ChevronDown size={14} className="text-muted" /> : <ChevronRight size={14} className="text-muted" />}
          <Folder size={14} className="text-accent" />
          <span className="truncate">{node.name}</span>
        </button>
        {isOpen && node.children && (
          <div className="pl-4 border-l border-light ml-2 flex flex-col gap-0.5 mt-0.5">
            {node.children.map((child) => (
              <FileTreeNodeItem
                key={child.path}
                node={child}
                onSelectFile={onSelectFile}
                selectedFilePath={selectedFilePath}
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
