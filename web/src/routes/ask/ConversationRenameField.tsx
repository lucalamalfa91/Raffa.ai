import { useEffect, useRef, useState } from "react";
import { CONVERSATION_NAME_MAX_LENGTH } from "./conversationTitle";

export interface ConversationRenameFieldProps {
  /** What the chat is called on screen right now; the field opens on it, fully selected. */
  initialValue: string;
  /** The accessible name, e.g. "Rename Atlassian — MSA". */
  label: string;
  /** Called with the typed name when it differs from `initialValue`; blank clears the name. */
  onCommit: (name: string) => void;
  onCancel: () => void;
  className?: string;
}

/**
 * Inline chat rename, shared by the rail and the chat header. Enter or leaving the field saves,
 * Escape cancels; an unchanged name cancels too, so opening the field and leaving it never stores
 * the automatic title as a name. Empty saves as "no name" -- the automatic title comes back.
 */
export default function ConversationRenameField({ initialValue, label, onCommit, onCancel, className }: ConversationRenameFieldProps) {
  const [value, setValue] = useState(initialValue);
  const inputRef = useRef<HTMLInputElement>(null);
  // Enter/Escape unmount the field, which blurs it: settle exactly once.
  const settled = useRef(false);

  useEffect(() => {
    inputRef.current?.focus();
    inputRef.current?.select();
  }, []);

  const finish = (save: boolean) => {
    if (settled.current) return;
    settled.current = true;
    if (save && value.trim() !== initialValue.trim()) onCommit(value);
    else onCancel();
  };

  return (
    <input
      ref={inputRef}
      type="text"
      className={`input conversation-rename-input${className ? ` ${className}` : ""}`}
      aria-label={label}
      value={value}
      maxLength={CONVERSATION_NAME_MAX_LENGTH}
      autoComplete="off"
      onChange={(event) => setValue(event.target.value)}
      onKeyDown={(event) => {
        if (event.key === "Enter") {
          event.preventDefault();
          finish(true);
        } else if (event.key === "Escape") {
          event.preventDefault();
          finish(false);
        }
      }}
      onBlur={() => finish(true)}
    />
  );
}
