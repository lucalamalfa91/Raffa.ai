using System.Runtime.CompilerServices;

// Raffa.Quotes.Tests asserts directly against this module's internal, dependency-free pure
// functions (QuoteLineNormalizationService.NormalizeUnitEconomics, SkuNormalizer.Normalize,
// SkuNormalizationService.Apply, ...: no JSON parsing, no DbContext) to prove them in isolation,
// without promoting them to this module's public API surface. Mirrors
// Raffa.Worker/AssemblyInfo.cs's identical "internal detail, but a real test still needs direct
// access" rationale. (QuoteLineExtractionService.ComputePricing, which this grant used to name,
// moved to Raffa.AiFlows with the service.)
[assembly: InternalsVisibleTo("Raffa.Quotes.Tests")]
