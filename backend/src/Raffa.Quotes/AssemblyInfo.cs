using System.Runtime.CompilerServices;

// Raffa.Quotes.Tests asserts directly against
// QuoteLineExtractionService.ComputePricing — an internal, dependency-free pure function (no
// JSON parsing, no DbContext) — to prove AC-3 ("separate arithmetic from LLM language") in
// isolation, without promoting it to this module's public API surface. Mirrors
// Raffa.Worker/AssemblyInfo.cs's identical "internal detail, but a real test still needs direct
// access" rationale.
[assembly: InternalsVisibleTo("Raffa.Quotes.Tests")]
