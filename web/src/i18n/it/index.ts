/**
 * Italian catalogue: English message id → Italian text. One file per area, merged here; the area
 * files only add entries, so a missing translation is a missing line in exactly one file.
 */

import { IT_CASE } from "./case";
import { IT_RENEWALS } from "./renewals";
import { IT_SHELL } from "./shell";
import { IT_ASK4 } from "./ask4";
import { IT_QUOTE4V } from "./quote4v";

export const IT: Readonly<Record<string, string>> = { ...IT_CASE, ...IT_RENEWALS, ...IT_SHELL, ...IT_ASK4, ...IT_QUOTE4V };
