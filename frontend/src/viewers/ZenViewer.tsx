import type { ReactNode } from "react";

export type ViewerKind = "ltp" | "signals" | "positions" | "watchlist";

export type ZenViewerProps = {
  title: string;
  subtitle?: string;
  kind: ViewerKind;
  loading?: boolean;
  error?: string | null;
  actions?: ReactNode;
  children: ReactNode;
};

/**
 * ZenViewer — thin presentation shell.
 * Receives already-fetched DTO rows from factories; never calls Angel or computes strategy.
 */
export function ZenViewer({
  title,
  subtitle,
  kind,
  loading,
  error,
  actions,
  children,
}: ZenViewerProps) {
  return (
    <section className={`zen-viewer zen-viewer--${kind}`} data-viewer={kind}>
      <header className="zen-viewer__header">
        <div>
          <h2 className="zen-viewer__title">{title}</h2>
          {subtitle ? <p className="zen-viewer__subtitle">{subtitle}</p> : null}
        </div>
        {actions ? <div className="zen-viewer__actions">{actions}</div> : null}
      </header>

      {loading ? <p className="zen-viewer__state">Loading…</p> : null}
      {error ? <p className="zen-viewer__error">{error}</p> : null}
      {!loading && !error ? <div className="zen-viewer__body">{children}</div> : null}
    </section>
  );
}

export function createZenViewer(kind: ViewerKind, title: string, subtitle?: string) {
  return function BoundZenViewer(
    props: Omit<ZenViewerProps, "kind" | "title" | "subtitle"> & {
      title?: string;
      subtitle?: string;
    },
  ) {
    return (
      <ZenViewer
        kind={kind}
        title={props.title ?? title}
        subtitle={props.subtitle ?? subtitle}
        loading={props.loading}
        error={props.error}
        actions={props.actions}
      >
        {props.children}
      </ZenViewer>
    );
  };
}

export const LtpViewer = createZenViewer("ltp", "Live LTP", "Shared cache from backend worker");
export const SignalsViewer = createZenViewer("signals", "Signals", "From analysis Run");
export const PositionsViewer = createZenViewer("positions", "Open positions", "Paper book");
export const WatchlistViewer = createZenViewer("watchlist", "Watchlist");
