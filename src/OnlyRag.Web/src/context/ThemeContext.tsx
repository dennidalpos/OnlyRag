import React, { createContext, useContext, useEffect, useState } from "react";

export type Theme = "dark" | "light" | "cyber" | "emerald";

export interface ThemeOption {
  id: Theme;
  name: string;
  icon: string;
  description: string;
}

export const THEME_OPTIONS: ThemeOption[] = [
  { id: "dark", name: "Scuro Midnight", icon: "🌙", description: "Modalità scura bilanciata ad alto contrasto per affaticamento visivo ridotto" },
  { id: "light", name: "Chiaro Crisp", icon: "☀️", description: "Modalità chiara e pulita ideale per ambienti molto illuminati" },
  { id: "cyber", name: "Cyberpunk Neon", icon: "⚡", description: "Tonalità viola-ciano futuristiche con contrasti vivaci" },
  { id: "emerald", name: "Obsidian Emerald", icon: "💎", description: "Sfondo ossidiana scuro con accenti verde smeraldo ed effetto vetro" }
];

interface ThemeContextType {
  theme: Theme;
  setTheme: (theme: Theme) => void;
  themes: ThemeOption[];
}

const STORAGE_KEY = "onlyrag_theme";

const ThemeContext = createContext<ThemeContextType | undefined>(undefined);

export function ThemeProvider({ children }: { children: React.ReactNode }) {
  const [theme, setThemeState] = useState<Theme>(() => {
    const saved = localStorage.getItem(STORAGE_KEY) as Theme | null;
    if (saved && ["dark", "light", "cyber", "emerald"].includes(saved)) {
      return saved;
    }
    return "dark";
  });

  const setTheme = (newTheme: Theme) => {
    setThemeState(newTheme);
    localStorage.setItem(STORAGE_KEY, newTheme);
    applyThemeToDOM(newTheme);
  };

  useEffect(() => {
    applyThemeToDOM(theme);
  }, [theme]);

  return (
    <ThemeContext.Provider value={{ theme, setTheme, themes: THEME_OPTIONS }}>
      {children}
    </ThemeContext.Provider>
  );
}

export function useTheme(): ThemeContextType {
  const context = useContext(ThemeContext);
  if (!context) {
    return {
      theme: (localStorage.getItem(STORAGE_KEY) as Theme) || "dark",
      setTheme: (newTheme: Theme) => {
        localStorage.setItem(STORAGE_KEY, newTheme);
        applyThemeToDOM(newTheme);
      },
      themes: THEME_OPTIONS
    };
  }
  return context;
}

function applyThemeToDOM(theme: Theme) {
  document.documentElement.setAttribute("data-theme", theme);
  const shell = document.querySelector(".desktop-shell");
  if (shell) {
    shell.setAttribute("data-theme", theme);
  }
}
