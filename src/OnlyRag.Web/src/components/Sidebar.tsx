export type SectionId = "chat" | "documents" | "jobs" | "translation" | "settings";

type SidebarProps = {
  activeSection: SectionId;
  sections: Record<SectionId, string>;
  onSectionChange: (section: SectionId) => void;
  activeJobCount?: number;
};

const sectionOrder: SectionId[] = ["chat", "documents", "jobs", "translation", "settings"];

export function Sidebar({ activeSection, sections, onSectionChange, activeJobCount = 0 }: SidebarProps) {
  return (
    <aside className="sidebar">
      <div className="brand">
        <img className="brand-mark" src="/favicon.svg" alt="" aria-hidden="true" />
        <p>OnlyRag</p>
      </div>
      <nav className="navigation" aria-label="Sezioni principali">
        {sectionOrder.map((section) => {
          const badge = section === "jobs" && activeJobCount > 0 ? activeJobCount : null;
          return (
            <button
              className={section === activeSection ? "nav-item nav-item--active" : "nav-item"}
              key={section}
              type="button"
              aria-current={section === activeSection ? "page" : undefined}
              onClick={() => onSectionChange(section)}
            >
              {sections[section]}
              {badge !== null && (
                <span className="nav-badge" aria-label={`${badge} operazioni attive`}>
                  {badge}
                </span>
              )}
            </button>
          );
        })}
      </nav>
    </aside>
  );
}
