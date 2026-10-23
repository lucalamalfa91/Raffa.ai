/**
 * The demo tenant's renewal cases at the start of the scenario (design prototype `seedCases`):
 * DocuSign waits for a decision on the discount, Microsoft is mid-interview, AWS has a policy
 * conflict to resolve. Salesforce has no case yet -- it is the "deadline without an owner" item.
 */

import { act, create } from "./engine";
import { DEMO_APPROVER, DEMO_OWNER, demoContext } from "./fixtures";
import type { ActPayload, ActionType, Case } from "./types";

type Step = [string, ActionType, ActPayload?];

function play(cid: string, steps: Step[], updated: string): Case {
  let c = create(cid, demoContext(cid, DEMO_APPROVER), null, { approver: DEMO_APPROVER, owner: DEMO_OWNER });
  for (const [who, type, payload] of steps) c = act(c, type, payload, demoContext(cid, who));
  const { clip: _clip, tick: _tick, ...rest } = c;
  void _clip;
  void _tick;
  return { ...rest, updated, seen: { [DEMO_OWNER]: c.seq, [DEMO_APPROVER]: 3 }, ui: { ...c.ui, warn: "" } };
}

const M = DEMO_OWNER;
const L = DEMO_APPROVER;
const sent = (eid: number, rid: number): Step[] => [
  [M, "toEx", { eid }],
  [L, "approve", { eid }],
  [M, "copy", { eid }],
  [M, "sentOpen", { eid }],
  [M, "sentOk", { eid }],
  [M, "remOk", { rid }],
];

export function seedDemoCases(): Record<string, Case> {
  return {
    "4": play("4", [[M, "ans", { q: 0, val: "keep" }], [M, "ans", { q: 1, val: "12" }], [M, "ans", { q: 2, val: "none" }], [M, "q3done"], [M, "confirmPlan"], ...sent(0, 0), [M, "sampleReply"], [M, "reply"], [M, "decide", { did: 0, opt: "b" }]], "07 Sep 2026"),
    "2": play("2", [[M, "ans", { q: 0, val: "keep" }], [M, "ans", { q: 1, val: "36" }], [M, "ans", { q: 2, val: "p5" }]], "30 Aug 2026"),
    "3": play(
      "3",
      [
        [M, "ans", { q: 0, val: "keep" }], [M, "ans", { q: 1, val: "24" }], [M, "ans", { q: 2, val: "none" }], [M, "policy"], [M, "q3done"], [M, "confirmPlan"],
        ...sent(0, 0), [M, "sampleReply"], [M, "reply"],
        [L, "approve", { eid: 1 }], [M, "copy", { eid: 1 }], [M, "sentOpen", { eid: 1 }], [M, "sentOk", { eid: 1 }], [M, "remOk", { rid: 1 }],
        [M, "sampleReply"], [M, "reply"],
      ],
      "05 Sep 2026",
    ),
  };
}
