import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it } from "vitest";
import { CodingSection } from "./CodingSection";
import { mockApi } from "../test/apiMock";
import { createModel } from "../test/fixtures";

describe("CodingSection", () => {
  it("renders correctly with audit presets, mode switch, and windows folder picker", async () => {
    mockApi([
      {
        path: "/api/workspace/config",
        method: "GET",
        response: {
          rootPath: null,
          isAuthorized: false,
          canRead: false,
          canWrite: false,
          fileCount: 0,
          lastVerifiedAt: null
        }
      }
    ]);

    render(
      <CodingSection
        models={[createModel({ name: "qwen2.5-coder" })]}
        defaultModel="qwen2.5-coder"
      />
    );

    expect(screen.getByRole("heading", { name: /Coding & Vibe Hub/i })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /Sfoglia Cartella \(Windows\)/i })).toBeInTheDocument();
    expect(screen.getByText(/🎨 Audit UI\/UX/i)).toBeInTheDocument();
    expect(screen.getByText(/⚙️ Audit Pipeline/i)).toBeInTheDocument();
    expect(screen.getByText(/📚 Audit Documentazione/i)).toBeInTheDocument();
    expect(screen.getByText(/🛠️ Audit Script Build \/ Package/i)).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /📖 Lettura \/ Piano/i })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /✍️ Scrittura/i })).toBeInTheDocument();
  });

  it("sends prompt and renders message stream in vibe chat", async () => {
    const user = userEvent.setup();
    mockApi([
      {
        path: "/api/workspace/config",
        method: "GET",
        response: {
          rootPath: null,
          isAuthorized: false,
          canRead: false,
          canWrite: false,
          fileCount: 0,
          lastVerifiedAt: null
        }
      },
      {
        path: "/api/coding/generate-stream",
        method: "POST",
        response: 'data: {"chunk":"public class Calculator {}"}\n\ndata: [DONE]\n\n'
      }
    ]);

    render(
      <CodingSection
        models={[createModel({ name: "qwen2.5-coder" })]}
        defaultModel="qwen2.5-coder"
      />
    );

    const promptInput = screen.getByPlaceholderText(/Modalità SCRITTURA/i);
    await user.type(promptInput, "Crea Calculator");

    const sendBtn = screen.getByRole("button", { name: /Invia \(Ctrl\+Enter\)/i });
    await user.click(sendBtn);

    await waitFor(() => {
      expect(screen.getByText("public class Calculator {}")).toBeInTheDocument();
    });
  });
});
