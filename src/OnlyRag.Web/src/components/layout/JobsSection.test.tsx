import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it } from "vitest";
import { createJob } from "../../test/fixtures";
import { mockApi } from "../../test/apiMock";
import { JobsSection } from "./JobsSection";

describe("JobsSection", () => {
  it("gives repeated row actions contextual accessible names", async () => {
    mockApi([
      {
        path: "/api/jobs?limit=100",
        response: [
          createJob({ id: "job-1", type: "document-ingestion", status: "Running" }),
          createJob({ id: "job-2", type: "document-translation", status: "Running" })
        ]
      }
    ]);

    render(<JobsSection />);

    const pauseButton = await screen.findByRole("button", { name: "Metti in pausa Importazione documento job-1" });
    expect(pauseButton).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Annulla Traduzione documento job-2" })).toBeInTheDocument();

    await userEvent.tab();

    expect(pauseButton).toHaveFocus();
  });

  it("announces job action failures", async () => {
    mockApi([
      {
        path: "/api/jobs?limit=100",
        response: [createJob({ id: "job-1", type: "document-ingestion", status: "Running" })]
      },
      {
        path: "/api/jobs/job-1/pause",
        method: "POST",
        status: 500,
        response: { detail: "Errore backend." }
      }
    ]);

    render(<JobsSection />);

    await userEvent.click(await screen.findByRole("button", { name: "Metti in pausa Importazione documento job-1" }));

    await waitFor(() => {
      expect(screen.getByRole("alert")).toHaveTextContent("Operazione non riuscita.");
    });
  });

  it("labels Ollama model installation jobs", async () => {
    mockApi([
      {
        path: "/api/jobs?limit=100",
        response: [
          createJob({
            id: "pull-1",
            type: "ollama-model-pull",
            status: "Running",
            currentStep: "downloading",
            progressPercent: 42
          })
        ]
      }
    ]);

    render(<JobsSection />);

    expect(await screen.findByText("Installazione modello Ollama")).toBeInTheDocument();
    expect(screen.getByLabelText("Avanzamento 42%")).toBeInTheDocument();
  });
});
