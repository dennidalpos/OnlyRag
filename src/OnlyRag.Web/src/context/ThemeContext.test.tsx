import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, beforeEach } from "vitest";
import { ThemeProvider, useTheme } from "./ThemeContext";

function TestConsumer() {
  const { theme, setTheme, themes } = useTheme();
  return (
    <div>
      <span data-testid="current-theme">{theme}</span>
      {themes.map((t) => (
        <button key={t.id} onClick={() => setTheme(t.id)}>
          {t.name}
        </button>
      ))}
    </div>
  );
}

describe("ThemeContext", () => {
  beforeEach(() => {
    localStorage.clear();
    document.documentElement.removeAttribute("data-theme");
  });

  it("defaults to dark theme when no saved preference exists", () => {
    render(
      <ThemeProvider>
        <TestConsumer />
      </ThemeProvider>
    );

    expect(screen.getByTestId("current-theme")).toHaveTextContent("dark");
    expect(document.documentElement.getAttribute("data-theme")).toBe("dark");
  });

  it("loads theme from localStorage if valid", () => {
    localStorage.setItem("onlyrag_theme", "cyber");

    render(
      <ThemeProvider>
        <TestConsumer />
      </ThemeProvider>
    );

    expect(screen.getByTestId("current-theme")).toHaveTextContent("cyber");
    expect(document.documentElement.getAttribute("data-theme")).toBe("cyber");
  });

  it("updates theme state, localStorage, and DOM attribute when setTheme is called", async () => {
    const user = userEvent.setup();

    render(
      <ThemeProvider>
        <TestConsumer />
      </ThemeProvider>
    );

    const lightButton = screen.getByRole("button", { name: "Chiaro Crisp" });
    await user.click(lightButton);

    expect(screen.getByTestId("current-theme")).toHaveTextContent("light");
    expect(localStorage.getItem("onlyrag_theme")).toBe("light");
    expect(document.documentElement.getAttribute("data-theme")).toBe("light");
  });

  it("provides fallback theme when useTheme is used outside ThemeProvider", () => {
    render(<TestConsumer />);
    expect(screen.getByTestId("current-theme")).toHaveTextContent("dark");
  });
});
