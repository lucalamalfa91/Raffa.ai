import { getPipelineStageViews } from "./uploadPipeline";

export interface ProcessingPipelineProps {
  currentStepIndex: number;
}

/**
 * AC-2: 6-stage list, current stage pulsing. Stage labels/order/treatment
 * are quoted from the compiled prototype's own `pipeLabels`/`pipeline`
 * mapping (inputs/design/prototypes/day1-demo.html) -- ADR-020's pixel
 * reference -- see uploadPipeline.ts for the full citation. Done stages
 * render in ink with a filled dot, pending stages in muted neutral, matching
 * the prototype's `fg`/`dot` ternary; only the current stage's dot pulses
 * (`prefers-reduced-motion` disables it, the same convention
 * styles/components.css's skeleton bars and signin.css's redirect spinner
 * already use). Dots are square, not circular -- the compiled prototype's
 * own dot span sets no `border-radius`, matching ADR-019's "zero corner
 * radius everywhere" (the one deliberate exception in this app, `.radio`,
 * is not this).
 */
export default function ProcessingPipeline({ currentStepIndex }: ProcessingPipelineProps) {
  const stages = getPipelineStageViews(currentStepIndex);

  return (
    <div className="pipeline-list" role="status" aria-live="polite">
      <p className="screen-kicker">Processing</p>
      {stages.map((stage) => (
        <div className={`pipeline-stage pipeline-stage--${stage.state}`} key={stage.label}>
          <span className="pipeline-stage-dot" aria-hidden="true" />
          <span>{stage.label}</span>
        </div>
      ))}
    </div>
  );
}
