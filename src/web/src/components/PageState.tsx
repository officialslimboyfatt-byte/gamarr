interface PageStateProps {
  title: string;
  description: string;
  actionLabel?: string;
  onAction?: () => void;
  tone?: "loading" | "empty" | "error";
}

export function PageState({
  title,
  description,
  actionLabel,
  onAction,
  tone = "empty"
}: PageStateProps) {
  return (
    <div className={`page-state ${tone}`}>
      <div className="page-state-badge">{tone}</div>
      <h3>{title}</h3>
      <p>{description}</p>
      {actionLabel && onAction ? (
        <button className="secondary-button" type="button" onClick={onAction}>
          {actionLabel}
        </button>
      ) : null}
    </div>
  );
}
