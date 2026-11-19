import { useEffect, useRef, useState, type FormEvent, type KeyboardEvent } from "react";
import { computeRenewalCost, formatAmount, formatCompact } from "./renewalCost";
import "./landing.css";

/** What the "Free renewal review" form sends (`POST /api/public/renewal-reviews`). */
export interface RenewalReviewRequest {
  name: string;
  email: string;
  company: string;
  renewal: string;
  consent: boolean;
  /** Honeypot: hidden from people, filled in by bots. The server drops a request that has it. */
  website: string;
}

export type RenewalReviewResult = { ok: true } | { ok: false; error: string };

export interface LandingPageProps {
  submitReview: (request: RenewalReviewRequest) => Promise<RenewalReviewResult>;
  /** Where "Sign in" goes: the app's sign-in screen. */
  signInHref?: string;
}

const SCREENS = [
  {
    name: "Dashboard",
    file: "dashboard",
    title: "See what your software really costs.",
    description: "Track spend, future commitments and automatic renewals in one view.",
    alt: "Actual Raffa Spend and savings dashboard with demonstration contract totals and a fiscal-year spend chart.",
  },
  {
    name: "Contract Intelligence",
    file: "contracts",
    title: "The clauses. Captured. Cited.",
    description: "Price increases, notice periods, liability caps and SLA credits—extracted with a source reference.",
    alt: "Actual Raffa clause extraction for the synthetic Ivorygate contract: auto-renewal, 5% uplift cap, liability and SLA terms, each referenced to page 1.",
  },
  {
    name: "Ask Raffa",
    file: "ask",
    title: "Ask a question. Get the contract evidence.",
    description: "See what to challenge before renewal, with the terms and sources behind the answer.",
    alt: "Actual Ask Raffa conversation about the synthetic Ivorygate contract, showing a question and sourced renewal analysis.",
  },
  {
    name: "Renewal Intelligence",
    file: "renewals",
    title: "Act before the notice window closes.",
    description: "See deadlines, ownership and the next step for every renewal.",
    alt: "Real Raffa Renewals screen listing synthetic contracts, notice dates and next actions.",
  },
] as const;

const RISKS = [
  {
    number: "01",
    tag: "PRICE UPLIFTS",
    title: "The increase that repeats.",
    body: "A small annual uplift becomes a larger bill. See the cumulative cost and decide what to challenge.",
    action: "Question the uplift. Negotiate a cap.",
  },
  {
    number: "02",
    tag: "AUTO-RENEWAL",
    title: "The deadline before the deadline.",
    body: "Your cancellation window can close months before the contract ends. Bring that decision forward.",
    action: "Keep your option to renegotiate or leave.",
  },
  {
    number: "03",
    tag: "CONTRACT TERMS",
    title: "The cost in the small print.",
    body: "Payment commitments, renewal terms and service credits can change the value of a deal.",
    action: "Put the terms on the negotiation agenda.",
  },
] as const;

export const RENEWAL_TIMINGS = [
  "Within 30 days",
  "1–3 months",
  "3–6 months",
  "More than 6 months",
  "Not sure yet",
] as const;

const CURRENCIES = ["CHF", "EUR", "USD", "GBP"] as const;

function Arrow({ down = false }: { down?: boolean }) {
  return (
    <svg
      aria-hidden="true"
      className={down ? "arrow down" : "arrow"}
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth="1.5"
    >
      <path d={down ? "M12 4v16m-6-6 6 6 6-6" : "M5 19 19 5M5 5h14v14"} />
    </svg>
  );
}

function imageSrc(file: string): string {
  return `/landing/${file}.jpg`;
}

export default function LandingPage({ submitReview, signInHref = "/signin" }: LandingPageProps) {
  const [screen, setScreen] = useState(0);
  const productDialog = useRef<HTMLDialogElement>(null);
  const privacyDialog = useRef<HTMLDialogElement>(null);
  const [spendInput, setSpendInput] = useState("100000");
  const [uplift, setUplift] = useState(2);
  const [years, setYears] = useState(3);
  const [currency, setCurrency] = useState<string>("CHF");
  const [formState, setFormState] = useState<"idle" | "sending" | "success" | "error">("idle");
  const [formError, setFormError] = useState("");

  // The page's in-page links (#product, #review, ...) scroll smoothly, as on the original site;
  // the app itself keeps the browser default.
  useEffect(() => {
    const root = document.documentElement;
    const previousBehavior = root.style.scrollBehavior;
    const previousPadding = root.style.scrollPaddingTop;
    const previousTitle = document.title;
    root.style.scrollBehavior = "smooth";
    root.style.scrollPaddingTop = "26px";
    document.title = "Raffa — Your next renewal deserves a closer look.";
    return () => {
      root.style.scrollBehavior = previousBehavior;
      root.style.scrollPaddingTop = previousPadding;
      document.title = previousTitle;
    };
  }, []);

  const spend = Number(spendInput);
  const spendValid = spendInput !== "" && Number.isFinite(spend) && spend >= 0 && spend <= 1e9;
  const cost = computeRenewalCost(spendValid ? spend : 0, uplift, years);
  const money = (value: number, decimals = 0) => `${currency} ${formatAmount(value, decimals)}`;
  const active = SCREENS[screen];

  const onTabKeyDown = (event: KeyboardEvent<HTMLButtonElement>) => {
    if (!["ArrowLeft", "ArrowRight", "Home", "End"].includes(event.key)) return;
    event.preventDefault();
    const next =
      event.key === "Home"
        ? 0
        : event.key === "End"
          ? SCREENS.length - 1
          : (screen + (event.key === "ArrowRight" ? 1 : SCREENS.length - 1)) % SCREENS.length;
    setScreen(next);
    document.getElementById(`tab-${next}`)?.focus();
  };

  async function onSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const form = event.currentTarget;
    const data = new FormData(form);
    setFormState("sending");
    setFormError("");
    const result = await submitReview({
      name: String(data.get("name") ?? ""),
      email: String(data.get("email") ?? ""),
      company: String(data.get("company") ?? ""),
      renewal: String(data.get("renewal") ?? ""),
      consent: data.get("consent") === "on",
      website: String(data.get("website") ?? ""),
    }).catch(() => ({ ok: false as const, error: "Your request could not be saved. Please try again." }));
    if (result.ok) {
      setFormState("success");
      form.reset();
    } else {
      setFormError(result.error);
      setFormState("error");
    }
  }

  return (
    <div className="raffa-landing" data-testid="landing-page">
      <a className="skip" href="#main">
        Skip to content
      </a>
      <header className="header wrap">
        <a href="#" className="brand" aria-label="Raffa home">
          <span className="brand-square" />
          raffa
          <span className="brand-dot">.</span>
        </a>
        <nav aria-label="Main navigation">
          <a href="#product">Product</a>
          <a href="#calculator">The hidden cost</a>
          <a className="nav-signin" href={signInHref}>
            Sign in
          </a>
          <a className="button nav-cta" href="#review">
            Find your savings <Arrow />
          </a>
        </nav>
      </header>

      <main id="main">
        <section className="hero wrap">
          <p className="eyebrow">
            <span className="tiny-square" /> SOFTWARE RENEWAL INTELLIGENCE
          </p>
          <h1>
            Same software.
            <br />
            <span>Higher bill.</span>
          </h1>
          <p className="hero-copy">
            Software renewals cost more than you think.
            <br className="desktop-break" /> Raffa finds hidden costs in price increases and contract clauses—so you
            know what to negotiate.
          </p>
          <div className="hero-actions">
            <a className="button primary" href="#review">
              Find your savings <Arrow />
            </a>
            <a className="text-link" href="#product">
              See Raffa in action <Arrow down />
            </a>
          </div>
          <p className="micro">Start with a free renewal review. One contract. A clearer next move.</p>
        </section>

        <section id="product" className="product wrap" aria-labelledby="product-heading">
          <div className="section-top">
            <p className="eyebrow">THE PRODUCT, IN PRACTICE</p>
            <span className="demo-label">
              <span /> Live MVP · Demo data
            </span>
          </div>
          <div className="product-tabs" role="tablist" aria-label="Explore Raffa screens">
            {SCREENS.map((item, index) => (
              <button
                key={item.file}
                type="button"
                role="tab"
                id={`tab-${index}`}
                aria-controls="product-panel"
                aria-selected={screen === index}
                tabIndex={screen === index ? 0 : -1}
                onKeyDown={onTabKeyDown}
                onClick={() => setScreen(index)}
              >
                <span className="tab-number">0{index + 1}</span>
                {item.name}
              </button>
            ))}
          </div>
          <div id="product-panel" role="tabpanel" aria-labelledby={`tab-${screen}`}>
            <div className="screen-shell">
              <div className="screen-top">
                <div className="window-dots" aria-hidden="true">
                  <i />
                  <i />
                  <i />
                </div>
                <span>RAFFA / {active.name.toUpperCase()}</span>
                <button
                  type="button"
                  onClick={() => productDialog.current?.showModal()}
                  aria-label={`Enlarge ${active.name} screenshot`}
                >
                  Expand <Arrow />
                </button>
              </div>
              <button
                type="button"
                className="screen-image"
                onClick={() => productDialog.current?.showModal()}
                aria-label={`View full ${active.name} screenshot`}
              >
                <img
                  src={imageSrc(active.file)}
                  alt={active.alt}
                  width="1340"
                  height="800"
                  fetchPriority={screen === 0 ? "high" : "auto"}
                />
              </button>
            </div>
            <div className="product-caption">
              <div>
                <h2 id="product-heading">{active.title}</h2>
                <p>{active.description}</p>
              </div>
              <span>
                Actual MVP. Synthetic demo data. <br />
                Explore each screen. Expand for detail.
              </span>
            </div>
          </div>
        </section>

        <section id="calculator" className="calculator-section" aria-labelledby="calc-heading">
          <div className="wrap">
            <div className="calc-heading">
              <div>
                <p className="eyebrow">SMALL PERCENTAGE. REAL MONEY.</p>
                <h2 id="calc-heading">
                  “It’s only 2%.”
                  <br />
                  <span>Every. Single. Year.</span>
                </h2>
              </div>
              <p>
                Price increases compound.
                <br />
                See what doing nothing could cost.
              </p>
            </div>
            <div className="calculator">
              <div className="calc-inputs">
                <label htmlFor="spend">Your current annual software cost</label>
                <div className="spend-input">
                  <select aria-label="Currency" value={currency} onChange={(event) => setCurrency(event.target.value)}>
                    {CURRENCIES.map((code) => (
                      <option key={code}>{code}</option>
                    ))}
                  </select>
                  <input
                    id="spend"
                    type="number"
                    inputMode="decimal"
                    min="0"
                    max="1000000000"
                    step="any"
                    value={spendInput}
                    onChange={(event) => setSpendInput(event.target.value)}
                    aria-describedby="spend-error"
                  />
                </div>
                <p id="spend-error" className="input-error">
                  {spendValid ? " " : "Enter an annual cost from 0 to 1 billion."}
                </p>
                <div className="slider-label">
                  <label htmlFor="uplift">Annual price increase</label>
                  <output htmlFor="uplift">{uplift}%</output>
                </div>
                <input
                  className="slider"
                  id="uplift"
                  type="range"
                  min="0"
                  max="15"
                  step="0.5"
                  value={uplift}
                  onChange={(event) => setUplift(Number(event.target.value))}
                />
                <div className="range-labels">
                  <span>0%</span>
                  <span>15%</span>
                </div>
                <div className="slider-label">
                  <label htmlFor="years">Next renewal years</label>
                  <output htmlFor="years">
                    {years} {years === 1 ? "year" : "years"}
                  </output>
                </div>
                <input
                  className="slider"
                  id="years"
                  type="range"
                  min="1"
                  max="10"
                  step="1"
                  value={years}
                  onChange={(event) => setYears(Number(event.target.value))}
                />
                <div className="range-labels">
                  <span>1 year</span>
                  <span>10 years</span>
                </div>
                <button
                  className="reset"
                  type="button"
                  onClick={() => {
                    setSpendInput("100000");
                    setUplift(2);
                    setYears(3);
                    setCurrency("CHF");
                  }}
                >
                  Reset to 2% over 3 years <span aria-hidden="true">↺</span>
                </button>
              </div>
              <div className="calc-result" aria-live="polite" aria-atomic="true">
                <p className="eyebrow">CUMULATIVE EXTRA COST</p>
                <p className="big-number" data-testid="landing-extra-cost">
                  <span>{currency}</span>{" "}
                  {spendValid ? (cost.extra >= 1e7 ? formatCompact(cost.extra) : formatAmount(cost.extra)) : "—"}
                </p>
                <p className="result-context">
                  {spendValid
                    ? `Over ${years} ${years === 1 ? "renewal year" : "renewal years"}, compared with keeping today’s price.`
                    : "Enter your annual cost to see the result."}
                </p>
                <div className={years > 5 ? "bars dense" : "bars"} aria-label="Annual price comparison">
                  {[{ year: 0, annual: spendValid ? spend : 0, extra: 0 }, ...cost.rows].map((row) => (
                    <div className="bar-group" key={row.year}>
                      <span className="bar-amount">
                        {years <= 5 && row.annual < 1e7 ? formatAmount(row.annual) : formatCompact(row.annual)}
                      </span>
                      <div className="bar-track">
                        <div
                          className="bar-fill"
                          style={{
                            height: `${spend > 0 && spendValid ? (row.annual / cost.rows[years - 1].annual) * 100 : 0}%`,
                          }}
                        >
                          <div
                            className="bar-extra"
                            style={{ height: `${row.annual ? (row.extra / row.annual) * 100 : 0}%` }}
                          />
                        </div>
                      </div>
                      <span className="bar-label">{row.year === 0 ? "Today" : `Y${row.year}`}</span>
                    </div>
                  ))}
                </div>
                <div className="calc-totals">
                  <div>
                    <span>Total with increases</span>
                    <strong>{spendValid ? money(cost.total) : "—"}</strong>
                  </div>
                  <div>
                    <span>Annual price after year {years}</span>
                    <strong>+{cost.finalIncrease.toFixed(2)}%</strong>
                  </div>
                </div>
              </div>
            </div>
            <div className="calc-foot">
              <p>
                Illustration, not a savings promise. First increase applies at the next renewal; another applies each
                year. Same scope, no taxes or discounts. Currency selection changes the unit, not an exchange rate.
              </p>
              <details>
                <summary>
                  See the calculation <span aria-hidden="true">+</span>
                </summary>
                <div className="calculation-detail">
                  <p>
                    Annual cost in year n = current cost × (1 + uplift)ⁿ. Extra cost = the sum of years 1–{years}, minus{" "}
                    {years} × today’s annual cost.
                  </p>
                  <div className="table-scroll">
                    <table>
                      <caption className="sr-only">Annual and cumulative uplift costs in {currency}</caption>
                      <thead>
                        <tr>
                          <th>Renewal year</th>
                          <th>Annual cost</th>
                          <th>Extra vs today</th>
                        </tr>
                      </thead>
                      <tbody>
                        {cost.rows.map((row) => (
                          <tr key={row.year}>
                            <th>Year {row.year}</th>
                            <td>{money(row.annual, 2)}</td>
                            <td>{money(row.extra, 2)}</td>
                          </tr>
                        ))}
                      </tbody>
                      <tfoot>
                        <tr>
                          <th>Total</th>
                          <td>{money(cost.total, 2)}</td>
                          <td>{money(cost.extra, 2)}</td>
                        </tr>
                      </tfoot>
                    </table>
                  </div>
                </div>
              </details>
            </div>
          </div>
        </section>

        <section id="opportunities" className="opportunities wrap" aria-labelledby="risks-heading">
          <div className="risk-intro">
            <p className="eyebrow">LOOK BEYOND THE PRICE</p>
            <h2 id="risks-heading">
              The expensive part
              <br />
              can be a sentence.
            </h2>
            <p>
              Know what you’re agreeing to.
              <br />
              Know what to ask for next.
            </p>
          </div>
          <div className="risk-list">
            {RISKS.map((risk) => (
              <article key={risk.number}>
                <span className="risk-number">{risk.number}</span>
                <div>
                  <p className="eyebrow">{risk.tag}</p>
                  <h3>{risk.title}</h3>
                  <p>{risk.body}</p>
                  <strong>
                    <Arrow /> {risk.action}
                  </strong>
                </div>
              </article>
            ))}
          </div>
        </section>

        <section id="review" className="review-section" aria-labelledby="review-heading">
          <div className="wrap review-grid">
            <div className="review-copy">
              <p className="eyebrow">YOUR NEXT RENEWAL IS A GOOD PLACE TO START.</p>
              <h2 id="review-heading">
                Find what’s
                <br />
                worth
                <br />
                <span>challenging.</span>
              </h2>
              <p>
                Get a free renewal review. We’ll look at price increases, key clauses and the questions to bring to
                your next negotiation.
              </p>
              <div className="review-promise">
                <span>
                  01 <b>One software renewal</b>
                </span>
                <span>
                  02 <b>Costs and terms to question</b>
                </span>
                <span>
                  03 <b>A practical next step</b>
                </span>
              </div>
            </div>
            <div className="review-form-shell">
              {formState === "success" ? (
                <div className="success" role="status">
                  <span className="success-mark" aria-hidden="true">
                    ✓
                  </span>
                  <p className="eyebrow">REQUEST RECEIVED</p>
                  <h3>
                    Your renewal deserves
                    <br />a closer look.
                  </h3>
                  <p>
                    Your details have been saved for the Raffa team. We’ll contact you about the review and how to share
                    your contract.
                  </p>
                  <a className="text-link" href="#product">
                    Explore the product <Arrow />
                  </a>
                </div>
              ) : (
                <form onSubmit={(event) => void onSubmit(event)}>
                  <h3>Free renewal review</h3>
                  <p className="form-intro">Leave your details. No contract upload needed yet.</p>
                  <label htmlFor="name">Your name</label>
                  <input id="name" name="name" autoComplete="name" maxLength={120} required placeholder="Alex Taylor" />
                  <label htmlFor="email">Work email</label>
                  <input
                    id="email"
                    name="email"
                    type="email"
                    autoComplete="email"
                    maxLength={254}
                    required
                    placeholder="alex@company.com"
                  />
                  <div className="form-row">
                    <div>
                      <label htmlFor="company">Company</label>
                      <input
                        id="company"
                        name="company"
                        autoComplete="organization"
                        maxLength={160}
                        required
                        placeholder="Company name"
                      />
                    </div>
                    <div>
                      <label htmlFor="renewal">Next renewal</label>
                      <select id="renewal" name="renewal" defaultValue="" required>
                        <option value="" disabled>
                          Select timing
                        </option>
                        {RENEWAL_TIMINGS.map((timing) => (
                          <option key={timing}>{timing}</option>
                        ))}
                      </select>
                    </div>
                  </div>
                  <div className="honeypot" aria-hidden="true">
                    <label htmlFor="website">Website</label>
                    <input id="website" name="website" tabIndex={-1} autoComplete="off" />
                  </div>
                  <label className="consent">
                    <input type="checkbox" name="consent" required />
                    <span>
                      I agree that Raffa may contact me about this review.{" "}
                      <button type="button" className="privacy-link" onClick={() => privacyDialog.current?.showModal()}>
                        How we use your details
                      </button>
                    </span>
                  </label>
                  <button className="button form-submit" type="submit" disabled={formState === "sending"}>
                    {formState === "sending" ? "Saving your request…" : "Find your savings"} <Arrow />
                  </button>
                  <p className="form-note">Free review. No commitment. No guaranteed savings.</p>
                  {formState === "error" && (
                    <p role="alert" className="form-error">
                      {formError}
                    </p>
                  )}
                </form>
              )}
            </div>
          </div>
        </section>
      </main>

      <footer className="footer wrap">
        <a href="#" className="brand" aria-label="Raffa home">
          <span className="brand-square" />
          raffa.
        </a>
        <p>A better position at your next renewal.</p>
        <button type="button" onClick={() => privacyDialog.current?.showModal()}>
          Privacy
        </button>
        <span>© {new Date().getFullYear()} Raffa</span>
      </footer>

      <dialog
        ref={productDialog}
        className="product-dialog"
        aria-label={`${active.name} product screenshot`}
        onClick={(event) => {
          if (event.target === event.currentTarget) productDialog.current?.close();
        }}
      >
        <div className="dialog-top">
          <span>{active.name} · Actual MVP · Demo data</span>
          <button type="button" onClick={() => productDialog.current?.close()} aria-label="Close screenshot">
            Close ×
          </button>
        </div>
        <div className="dialog-image">
          <img src={imageSrc(active.file)} alt={active.alt} />
        </div>
        <p>Synthetic demonstration data. Captured from Raffa on 13 November 2026.</p>
      </dialog>

      <dialog ref={privacyDialog} className="privacy-dialog" aria-labelledby="privacy-title">
        <div className="dialog-top">
          <span>YOUR DETAILS</span>
          <button type="button" onClick={() => privacyDialog.current?.close()} aria-label="Close privacy notice">
            Close ×
          </button>
        </div>
        <h2 id="privacy-title">Just for your review.</h2>
        <p>
          We collect your name, email, company and renewal timing to respond to your renewal review request. Your
          submission is stored for the Raffa team; it is not displayed on this website.
        </p>
        <p>
          This form does not subscribe you to marketing emails. Do not submit confidential contract content here. To
          request deletion of your submission, reply to the team when they contact you.
        </p>
        <button className="button primary" type="button" onClick={() => privacyDialog.current?.close()}>
          Back to the page <Arrow />
        </button>
      </dialog>
    </div>
  );
}
