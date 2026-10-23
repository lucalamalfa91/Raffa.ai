import type { QueueItem, RowActions, RowCta } from "../case/portfolio";
import type { Translate } from "../../../i18n/translate";
import QueueRow from "./QueueRow";

export interface QueueGroupsProps {
  groups: ReadonlyArray<{ label: string; items: QueueItem[] }>;
  t: Translate;
  actionsFor: (item: QueueItem) => RowActions;
  onRun: (item: QueueItem, cta: RowCta) => void;
  onDelegate: (item: QueueItem, delegate: NonNullable<RowActions["delegate"]>) => void;
  onSnooze: (item: QueueItem) => void;
  onDone: (item: QueueItem) => void;
  onOpenInRenewals?: (item: QueueItem) => void;
}

/** Groups of queue rows under a 2px rule with their count. */
export default function QueueGroups({ groups, t, actionsFor, onRun, onDelegate, onSnooze, onDone, onOpenInRenewals }: QueueGroupsProps) {
  return (
    <>
      {groups.map((group) => (
        <section className="rn-qgroup" key={group.label} aria-label={group.label}>
          <h2 className="rn-qgroup-head">
            <span>{group.label}</span>
            <span className="rn-small">{group.items.length}</span>
          </h2>
          <ul className="rn-qlist">
            {group.items.map((item) => (
              <QueueRow
                key={item.key}
                item={item}
                actions={actionsFor(item)}
                t={t}
                onRun={(cta) => onRun(item, cta)}
                onDelegate={(d) => onDelegate(item, d)}
                onSnooze={() => onSnooze(item)}
                onDone={() => onDone(item)}
                onOpenInRenewals={onOpenInRenewals ? () => onOpenInRenewals(item) : undefined}
              />
            ))}
          </ul>
        </section>
      ))}
    </>
  );
}
