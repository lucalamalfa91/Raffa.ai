export interface ScaffoldScreenProps {
  title: string;
  release: string;
  note: string;
}

/**
 * Placeholder rendered for every rail route this task wires but does not
 * build the real screen for (Home, Portfolio, Contract 360, Renewals, Quote
 * check, Documents, Review queue) — each ships in its own later epic/feature
 * (see the `note` passed at each call site in WorkspaceShellApp.tsx). This
 * task's job is the shell chrome, the route guards, and the global Ask bar
 * scaffold, not those screens.
 *
 * Kept inside this task's own components/shell/ folder rather than under
 * src/routes/<name>/ so it never collides with those future tasks' own
 * declared file ownership (e.g. epic-06/feature-04-workspace-members-ui's
 * task already claims src/routes/workspace/members/).
 */
export default function ScaffoldScreen({ title, release, note }: ScaffoldScreenProps) {
  return (
    <div className="empty-state">
      <p className="screen-kicker">{release}</p>
      <h2 className="screen-title">{title}</h2>
      <p className="micro-meta">{note}</p>
    </div>
  );
}
