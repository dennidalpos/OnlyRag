export function SkeletonSection() {
  return (
    <div className="skeleton-container" aria-label="Caricamento in corso..." aria-busy="true">
      <div className="skeleton-box" style={{ height: "48px", width: "40%" }} />
      <div className="skeleton-box" style={{ height: "160px", width: "100%" }} />
      <div className="flex gap-4 mt-2" style={{ display: "flex", gap: "16px", marginTop: "8px" }}>
        <div className="skeleton-box" style={{ height: "120px", width: "50%" }} />
        <div className="skeleton-box" style={{ height: "120px", width: "50%" }} />
      </div>
      <div className="skeleton-box" style={{ height: "200px", width: "100%" }} />
    </div>
  );
}
