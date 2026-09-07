import { QUOTE_STEPS, type QuoteStepIndex } from "./quoteCheckViewModel";

export interface QuoteStepperProps {
  activeStep: QuoteStepIndex;
  onSelectStep: (step: QuoteStepIndex) => void;
}

/**
 * The 4-step header (screens.md #10 AC-1: "Stepper Extract -> Assessment -> Target ->
 * Negotiation"). Every step is directly clickable -- day1-demo.html's own `qsteps[i].go` jumps
 * straight to step `i` with no forward-gating at the nav level; only the Assessment step's own
 * *content* gates on `isAssessmentBlocked` (AC-2), rendered by `AssessmentStep.tsx`, not here.
 */
export default function QuoteStepper({ activeStep, onSelectStep }: QuoteStepperProps) {
  return (
    <div className="quote-stepper" role="tablist" aria-label="Quote check steps">
      {QUOTE_STEPS.map((step) => (
        <button
          key={step.index}
          type="button"
          role="tab"
          aria-selected={step.index === activeStep}
          className="quote-stepper-step"
          data-active={step.index === activeStep}
          onClick={() => onSelectStep(step.index)}
        >
          <span className="quote-stepper-number" aria-hidden="true">
            {step.number}
          </span>
          {step.label}
        </button>
      ))}
    </div>
  );
}
