using Raffa.AiFlows.Shared.Pack;

namespace Raffa.AiFlows.Ask.Packs;

/// <summary>Safe operating framework for broad procurement questions; contains no tenant claim or figure.</summary>
internal static class ProcurementAdvisoryPack
{
    public static PackItem Build() => new(
        "raffa:procurement-advisory", PackCorpus.Raffa, "Raffa procurement advisory framework",
        "method, recommendation and next-best action", null, null,
        "Separate validated facts, calculated indicators, market benchmarks, recommendations and assumptions. " +
        "For sourcing cover requirements, options, evaluation criteria, TCO, risk, negotiation and governance. " +
        "For supplier management cover performance, dependency, financial/operational/security risk, remediation and exit. " +
        "For category strategy cover demand, supply market, segmentation, levers, roadmap and measurable outcomes. " +
        "Proactively identify the next decision, deadline, missing input and reversible action. Mark every unsupported " +
        "dimension not_evaluable; never invent a supplier fact, score, benchmark, saving or deadline.",
        null, null, null, "Raffa versioned procurement method", []);
}
