export interface SegOption<T extends string> {
  value: T;
  label: string;
  /** Shown after the label, e.g. a count. */
  badge?: string | number;
}

export interface SegControlProps<T extends string> {
  label: string;
  value: T;
  options: ReadonlyArray<SegOption<T>>;
  onChange: (value: T) => void;
}

/** The design system's segmented control: native buttons, the selected one filled with ink. */
export default function SegControl<T extends string>({ label, value, options, onChange }: SegControlProps<T>) {
  return (
    <div className="seg" role="group" aria-label={label}>
      {options.map((option) => (
        <button key={option.value} type="button" aria-pressed={option.value === value} onClick={() => onChange(option.value)}>
          {option.label}
          {option.badge !== undefined && ` · ${option.badge}`}
        </button>
      ))}
    </div>
  );
}
