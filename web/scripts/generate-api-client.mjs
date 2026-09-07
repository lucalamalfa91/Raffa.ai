#!/usr/bin/env node
// Generates web/src/api/generated/schema.ts from web/openapi/contigo-api.v1.json.
//
// Why a first-party generator instead of a third-party OpenAPI-to-TypeScript
// tool (openapi-typescript, swagger-typescript-api, ...): see the
// "Codegen tool choice" paragraph in web/openapi/contigo-api.v1.json's
// info.description. In short: openapi-typescript@7.13.0's `typescript@^5.x`
// peer dependency hard-conflicts with this repo's already-committed
// `typescript@^7.0.2` (web/package.json, task E01/F07/US01/T01) under npm's
// default strict peer resolution -- verified here by running
// `npm install --save-dev openapi-typescript`, which failed with ERESOLVE.
// Rather than force an incorrect peer resolution or downgrade a previous
// task's already-committed TypeScript version, this script has zero
// dependencies of its own (Node built-ins only), so it can never hit that
// class of conflict. It emits the same `paths` / `operations` shape the
// mainstream tools in this ecosystem use, so swapping to a third-party
// generator later (once one supports TypeScript 7) only means deleting this
// script -- web/src/api/client.ts, which consumes that shape, does not
// change.
//
// Run: npm run generate:api (see web/package.json). `npm run build` also
// runs this first, so the committed output (schema.ts) can never silently
// drift from the contract (contigo-api.v1.json).

import { mkdirSync, readFileSync, writeFileSync } from "node:fs";
import { dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";

const scriptDir = dirname(fileURLToPath(import.meta.url));
const webRoot = resolve(scriptDir, "..");
const specPath = resolve(webRoot, "openapi", "contigo-api.v1.json");
const outPath = resolve(webRoot, "src", "api", "generated", "schema.ts");

// Only the methods OpenAPI 3.1's Path Item Object defines are ever worth
// scanning for; anything else in a path item (parameters, summary, ...) is
// not an operation and must be skipped.
const HTTP_METHODS = ["get", "put", "post", "delete", "options", "head", "patch", "trace"];

function readSpec(path) {
  let raw;
  try {
    raw = readFileSync(path, "utf-8");
  } catch (cause) {
    throw new Error(`Cannot read OpenAPI document at ${path}: ${cause.message}`);
  }

  let spec;
  try {
    spec = JSON.parse(raw);
  } catch (cause) {
    throw new Error(`${path} is not valid JSON: ${cause.message}`);
  }

  if (!spec || typeof spec !== "object" || !spec.paths || typeof spec.paths !== "object") {
    throw new Error(`${path} is not a valid OpenAPI document: missing a top-level "paths" object.`);
  }
  return spec;
}

/** Renders one OpenAPI Schema Object as a TypeScript type expression (single line, no trailing `;`). */
function renderSchemaType(schema) {
  if (!schema || typeof schema !== "object") return "unknown";
  if (Array.isArray(schema.enum) && schema.enum.length > 0) {
    return schema.enum.map((value) => JSON.stringify(value)).join(" | ");
  }
  // OpenAPI 3.1 nullable-by-union, e.g. `"type": ["string", "null"]` -- task E06/F01/US01/T01
  // added this for Document.ContractId (null until classification links a freshly-uploaded
  // document to a contract; see web/openapi/contigo-api.v1.json's info.description). Each member
  // is rendered as its own concrete type (or the literal `null`) and unioned; every schema this
  // file documents today unions exactly one concrete type with "null", but this loops over
  // however many members are actually present rather than assuming exactly two.
  if (Array.isArray(schema.type)) {
    return schema.type
      .map((oneType) => (oneType === "null" ? "null" : renderSchemaType({ ...schema, type: oneType })))
      .join(" | ");
  }
  switch (schema.type) {
    case "string":
      return "string";
    case "integer":
    case "number":
      return "number";
    case "boolean":
      return "boolean";
    // Task E08/F03/US01/T01 (quote-check-ui): parenthesized unconditionally, not just
    // `${renderSchemaType(schema.items)}[]` -- an array whose `items` schema is itself a union (the
    // `enum` branch above, e.g. `leversUsed`'s 7-member NegotiationLeverType vocabulary) rendered as
    // `"Volume" | "Term" | ... | "PaymentTerms"[]`, which TypeScript parses as `[]` binding tighter
    // than `|` (a union of six bare string-literal types and one *array* type, not an array of the
    // union) -- caught here before it reached a hand-written client.ts type and produced a real
    // `tsc --noEmit` mismatch the first time this file's `array-of-enum` combination actually arose
    // (every earlier array in this document, e.g. `comparisonDimensions: string[]`, has a
    // non-union item type, so this precedence bug never surfaced before). Parenthesizing is always
    // correct, including for the common non-union case (`(string)[]` is equivalent to `string[]`).
    case "array":
      return `(${renderSchemaType(schema.items)})[]`;
    case "object": {
      // Flat property maps only -- task E06/F01/US01/T01 (the workspace/document response bodies
      // this contract documents today are all a single flat level -- none of their own
      // properties are themselves an object). `required` marks a property non-optional; anything
      // absent from it renders with a `?` instead, the normal OpenAPI/JSON-Schema meaning. Task
      // E06/F03/US01/T01 (signin-workspace-picker) independently needed this same case for
      // `POST /api/workspaces`'s `201` body (id/name/createdAt) -- both tasks branched before the
      // other landed on integration; this is the merged implementation both rely on.
      // Extend this case (not the generated output by hand) when a future endpoint needs a
      // property whose value is itself an object, or a `$ref`/`oneOf` schema.
      if (!schema.properties || typeof schema.properties !== "object") return "unknown";
      const required = new Set(Array.isArray(schema.required) ? schema.required : []);
      const members = Object.entries(schema.properties).map(
        ([key, propSchema]) => `${key}${required.has(key) ? "" : "?"}: ${renderSchemaType(propSchema)}`,
      );
      return members.length > 0 ? `{ ${members.join("; ")} }` : "Record<string, never>";
    }
    default:
      // Not needed by any operation web/openapi/contigo-api.v1.json documents
      // today. Extend this switch (not the generated output by hand) when a
      // future endpoint needs `$ref`/oneOf support.
      return "unknown";
  }
}

const ind = (depth) => "  ".repeat(depth);

function renderOperation(pathTemplate, method, operation, depth) {
  if (!operation.operationId) {
    throw new Error(
      `${pathTemplate} ${method.toUpperCase()} has no operationId; every operation must declare one ` +
        "(it becomes this type's key in the generated `operations` interface).",
    );
  }

  const lines = [];
  lines.push(`${ind(depth)}${operation.operationId}: {`);
  lines.push(`${ind(depth + 1)}responses: {`);
  for (const [status, response] of Object.entries(operation.responses ?? {})) {
    lines.push(`${ind(depth + 2)}${status}: {`);
    lines.push(`${ind(depth + 3)}content: {`);
    for (const [mediaType, mediaObj] of Object.entries(response?.content ?? {})) {
      lines.push(`${ind(depth + 4)}"${mediaType}": ${renderSchemaType(mediaObj?.schema)};`);
    }
    lines.push(`${ind(depth + 3)}};`);
    lines.push(`${ind(depth + 2)}};`);
  }
  lines.push(`${ind(depth + 1)}};`);
  lines.push(`${ind(depth)}};`);
  return lines;
}

function generate(spec) {
  const operationLines = [];
  const pathLines = [];

  for (const [pathTemplate, pathItem] of Object.entries(spec.paths)) {
    const methodLines = [];
    for (const method of HTTP_METHODS) {
      const operation = pathItem[method];
      if (!operation) continue;

      operationLines.push(...renderOperation(pathTemplate, method, operation, 1));
      methodLines.push(`${ind(2)}${method}: operations["${operation.operationId}"];`);
    }

    if (methodLines.length === 0) continue;
    pathLines.push(`${ind(1)}"${pathTemplate}": {`);
    pathLines.push(...methodLines);
    pathLines.push(`${ind(1)}};`);
  }

  return (
    [
      "// AUTO-GENERATED by web/scripts/generate-api-client.mjs -- DO NOT EDIT BY HAND.",
      "// Source contract: web/openapi/contigo-api.v1.json (ADR-012, api-consumption.md",
      '// #1: "one generated TypeScript client, no hand-written divergent DTOs").',
      "// Regenerate: npm run generate:api (also runs automatically as part of",
      "// npm run build, so this file can never silently drift from the contract).",
      "",
      "export interface operations {",
      ...operationLines,
      "}",
      "",
      "export interface paths {",
      ...pathLines,
      "}",
      "",
    ]
      .join("\n")
  );
}

const spec = readSpec(specPath);
const output = generate(spec);
mkdirSync(dirname(outPath), { recursive: true });
writeFileSync(outPath, output, "utf-8");
console.log(`Generated ${outPath} from ${specPath}`);
