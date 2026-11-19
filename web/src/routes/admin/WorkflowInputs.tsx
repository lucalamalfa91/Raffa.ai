import { useState, type FormEvent } from "react";
import type { PlaygroundWorkflow, PlaygroundWorkflowInput } from "../../api/client";

const PURPOSES = ["MarketPractice", "SupplierNews", "BenchmarkRange", "NegotiationTactics", "Open"] as const;
const MAX_FILE_BYTES = 8 * 1024 * 1024;
const TEXT_TYPES = /\.(txt|md)$/i;

const MIME_BY_EXTENSION: Record<string, string> = { pdf: "application/pdf", png: "image/png", jpg: "image/jpeg", jpeg: "image/jpeg", tif: "image/tiff", tiff: "image/tiff" };

function toBase64(buffer: ArrayBuffer): string {
  let binary = "";
  const bytes = new Uint8Array(buffer);
  for (let i = 0; i < bytes.length; i += 0x8000) binary += String.fromCharCode(...bytes.subarray(i, i + 0x8000));
  return btoa(binary);
}

/** The input form of one workflow; `onRun` receives what the run endpoint takes as `input`. */
export default function WorkflowInputs({
  workflow,
  busy,
  lanes,
  onRun,
}: {
  workflow: Exclude<PlaygroundWorkflow, "ask">;
  busy: boolean;
  lanes: number;
  onRun: (input: PlaygroundWorkflowInput) => void;
}) {
  const [supplier, setSupplier] = useState("");
  const [goal, setGoal] = useState("");
  const [question, setQuestion] = useState("");
  const [language, setLanguage] = useState<"it" | "en">("en");
  const [query, setQuery] = useState("");
  const [purpose, setPurpose] = useState<string>("MarketPractice");
  const [text, setText] = useState("");
  const [file, setFile] = useState<File | null>(null);
  const [documentFirst, setDocumentFirst] = useState(true);
  const [problem, setProblem] = useState<string | null>(null);

  const ready =
    workflow === "research" ? query.trim().length > 0 : workflow === "extraction" ? text.trim().length > 0 || file !== null : supplier.trim().length > 0;

  const submit = async (event: FormEvent) => {
    event.preventDefault();
    setProblem(null);
    if (workflow === "research") return onRun({ query: query.trim(), purpose, language });
    if (workflow === "extraction") {
      if (file === null) return onRun({ text, documentFirst });
      if (file.size > MAX_FILE_BYTES) return setProblem("The file is larger than 8 MB.");
      if (TEXT_TYPES.test(file.name)) return onRun({ text: await file.text(), documentFirst });
      const mimeType = file.type || MIME_BY_EXTENSION[file.name.split(".").pop()?.toLowerCase() ?? ""] || "";
      if (!Object.values(MIME_BY_EXTENSION).includes(mimeType)) return setProblem("Use a .txt, .md, .pdf, .png, .jpg or .tiff file.");
      return onRun({ fileBase64: toBase64(await file.arrayBuffer()), fileName: file.name, mimeType, documentFirst });
    }
    return onRun({
      supplier: supplier.trim(),
      ...(goal.trim() ? { goal: goal.trim() } : {}),
      ...(question.trim() ? { question: question.trim() } : {}),
      language,
    });
  };

  return (
    <form className="pg-composer pg-inputs" onSubmit={(e) => void submit(e)} aria-label="Workflow input">
      {workflow === "council" || workflow === "drafting" ? (
        <>
          <div className="pg-field">
            <label htmlFor="pg-supplier">Supplier (a contract of the sandbox)</label>
            <input id="pg-supplier" className="input" value={supplier} placeholder="Salesforce" onChange={(e) => setSupplier(e.target.value)} />
          </div>
          <div className="pg-field">
            <label htmlFor="pg-goal">Savings goal (optional)</label>
            <input id="pg-goal" className="input" value={goal} placeholder="reduce 20k" onChange={(e) => setGoal(e.target.value)} />
          </div>
          <div className="pg-field">
            <label htmlFor="pg-wq">{workflow === "council" ? "Question (optional)" : "What to write (optional)"}</label>
            <input id="pg-wq" className="input" value={question} onChange={(e) => setQuestion(e.target.value)} />
          </div>
          {workflow === "drafting" ? (
            <div className="pg-field">
              <label htmlFor="pg-lang">Language</label>
              <select id="pg-lang" className="input" value={language} onChange={(e) => setLanguage(e.target.value === "it" ? "it" : "en")}>
                <option value="en">English</option>
                <option value="it">Italiano</option>
              </select>
            </div>
          ) : null}
        </>
      ) : null}

      {workflow === "research" ? (
        <>
          <div className="pg-field">
            <label htmlFor="pg-query">Web query</label>
            <input id="pg-query" className="input" value={query} placeholder="Salesforce price increases 2026" onChange={(e) => setQuery(e.target.value)} />
          </div>
          <div className="pg-field">
            <label htmlFor="pg-purpose">Purpose</label>
            <select id="pg-purpose" className="input" value={purpose} onChange={(e) => setPurpose(e.target.value)}>
              {PURPOSES.map((p) => (
                <option key={p} value={p}>
                  {p}
                </option>
              ))}
            </select>
          </div>
          <div className="pg-field">
            <label htmlFor="pg-rlang">Answer language</label>
            <select id="pg-rlang" className="input" value={language} onChange={(e) => setLanguage(e.target.value === "it" ? "it" : "en")}>
              <option value="en">English</option>
              <option value="it">Italiano</option>
            </select>
          </div>
          <p className="hint">The playground is the consent here: the workspace opt-in and the daily web budget are not applied. The query is cleaned like in production and only the cleaned words leave Raffa (shown in the result).</p>
        </>
      ) : null}

      {workflow === "extraction" ? (
        <>
          <div className="pg-field">
            <label htmlFor="pg-text">Contract text (a form feed starts a new page)</label>
            <textarea id="pg-text" className="input pg-prompt" rows={8} value={text} disabled={file !== null} onChange={(e) => setText(e.target.value)} />
          </div>
          <div className="pg-field">
            <label htmlFor="pg-file">…or a file (.txt, .md, .pdf, .png, .jpg, .tiff; read in memory, never stored)</label>
            <input id="pg-file" type="file" accept=".txt,.md,.pdf,.png,.jpg,.jpeg,.tif,.tiff" onChange={(e) => setFile(e.target.files?.[0] ?? null)} />
          </div>
          <label className="pg-lane-pick">
            <input type="checkbox" checked={documentFirst} onChange={(e) => setDocumentFirst(e.target.checked)} /> Document-first layout (as dev runs it)
          </label>
          <p className="hint">Runs the extraction stages on the text and shows each stage's raw payload. Nothing is saved to any contract.</p>
        </>
      ) : null}

      {problem ? (
        <p role="alert" className="admin-error">
          {problem}
        </p>
      ) : null}
      <button type="submit" className="btn btn-primary" disabled={busy || !ready}>
        {busy ? "Running…" : lanes > 1 ? `Run on ${lanes} variants` : "Run"}
      </button>
    </form>
  );
}
