import { render, screen, fireEvent } from "@testing-library/react";
import { describe, it, expect, vi } from "vitest";
import { WorkspaceFileTree, FileTreeNode } from "./WorkspaceFileTree";

describe("WorkspaceFileTree", () => {
  const sampleNodes: FileTreeNode[] = [
    {
      name: "src",
      path: "src",
      isDirectory: true,
      children: [
        { name: "App.tsx", path: "src/App.tsx", isDirectory: false },
        { name: "main.ts", path: "src/main.ts", isDirectory: false }
      ]
    },
    { name: "README.md", path: "README.md", isDirectory: false }
  ];

  it("renders empty workspace message when nodes list is empty", () => {
    render(<WorkspaceFileTree nodes={[]} />);
    expect(screen.getByText(/Nessun file nel workspace/i)).toBeInTheDocument();
  });

  it("renders node hierarchy and handles file selection", () => {
    const onSelectFile = vi.fn();
    render(<WorkspaceFileTree nodes={sampleNodes} onSelectFile={onSelectFile} />);

    expect(screen.getByText("src")).toBeInTheDocument();
    expect(screen.getByText("App.tsx")).toBeInTheDocument();

    fireEvent.click(screen.getByText("App.tsx"));
    expect(onSelectFile).toHaveBeenCalledWith("src/App.tsx");
  });

  it("filters nodes based on search query", () => {
    render(<WorkspaceFileTree nodes={sampleNodes} />);
    const searchInput = screen.getByPlaceholderText(/Cerca file nel workspace/i);

    fireEvent.change(searchInput, { target: { value: "README" } });

    expect(screen.getByText("README.md")).toBeInTheDocument();
    expect(screen.queryByText("main.ts")).not.toBeInTheDocument();
  });
});
