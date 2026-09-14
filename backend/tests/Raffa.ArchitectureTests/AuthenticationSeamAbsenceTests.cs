using System.Text.RegularExpressions;

namespace Raffa.ArchitectureTests;

/// <summary>
/// S-T23 (ADR-025 §J.8a/§J.9; NW-58r; answers OQ-w15-006): "A repository-wide search finds no
/// configuration key, environment variable or code path that skips, mints or reads an
/// authentication credential for test purposes." The w15 table closed NW-58r on a runbook step
/// (<c>docs/waves/w15-acceptance.md</c>, N3b) instead of a product test seam precisely because this
/// test exists — a rewritten skip reason in <c>web/e2e/invite.spec.ts</c> is prose, and prose
/// cannot stop a later wave from quietly adding the bypass this table refused. This test is that
/// stop, and it is the only part of NW-58r that runs in CI.
///
/// <para>
/// <b>Why a seam is refused everywhere, not just on <c>dev</c></b> (ADR-025 §J.8a): a code path
/// that mints, reads or short-circuits an authentication credential, gated only by configuration,
/// ships in the same image on every environment. <c>dev</c> and <c>demo</c> share one Entra
/// directory, so a seam "only on <c>dev</c>" is a seam against the directory <c>demo</c> trusts;
/// there is no Playwright runner in CI at all (wiring one is NW-50, W18), so the seam would ship
/// with nothing executing it — a bypass with no compensating check; and NW-05 lands in this same
/// wave, finally closing impersonation, which a quiet auth bypass would reopen.
/// </para>
///
/// <para>
/// <b>Scope</b> — <c>backend/src/**</c>, <c>web/src/**</c>, <c>infra/**</c> and
/// <c>.github/workflows/**</c>: the shipped product and the pipeline that deploys it. Deliberately
/// <b>not</b> <c>backend/tests/**</c> or <c>web/e2e/**</c> — test doubles legitimately fake, stub
/// or skip a credential there (<c>FixtureAiGateway</c> and friends), and this same task's own
/// <c>web/e2e/invite.spec.ts</c> names a real, ADR-025 §J.8b-approved one-time-passcode gap in
/// prose. This test bounds the <b>shipped image</b>, which is what ADR-025 §J.8a actually rules on.
/// </para>
///
/// <para>
/// <b>Three shapes, not a keyword blacklist</b> (<see cref="AuthenticationSeamScanner.Patterns"/>)
/// — each entry names the ADR-025 §J.8a shape it defends, and each requires its trigger words to
/// sit inside one compact identifier or one short clause, never bare proximity across a sentence
/// (which is what keeps this from flagging prose that discusses the ban itself — this very doc
/// comment included): <b>(1) a key that disables authentication</b> —
/// <c>skip</c>/<c>disable</c>/<c>bypass</c> beside
/// <c>auth</c>/<c>credential</c>/<c>token</c>/<c>password</c>/<c>login</c>/<c>mfa</c>/
/// <c>passcode</c>/<c>otp</c>, either order; <b>(2) a path that returns a fixed passcode or
/// token</b> — <c>test</c>/<c>e2e</c>/<c>fake</c>/<c>stub</c>/<c>dummy</c>/<c>mock</c>/
/// <c>hardcoded</c>/<c>fixed</c> beside <c>passcode</c>/<c>otp</c>/<c>token</c>/<c>password</c>/
/// <c>credential</c>, either order — this is also J.8a's "mints ... a credential for test
/// purposes"; <b>(3) a branch keyed on an environment name that skips a credential check</b> —
/// shape (1)'s pair, plus an environment name (<c>dev</c>, <c>e2e</c>, <c>demo</c>, <c>test</c>,
/// <c>staging</c>, <c>local</c>) on the same line. Verified against the current tree with zero
/// matches before this test was added (six patterns, six separate scans) — this is additive, and a
/// match means someone introduced the shape, not that the pattern is over-broad.
/// </para>
///
/// <para>
/// <see cref="AuthenticationSeamAbsenceSelfTests"/> proves the scanner itself is not vacuously
/// true — the same relationship <see cref="ArchitectureRuleEnforcementTests"/> has to
/// <see cref="DependencyDirectionTests"/>: a negative test that always passes because its detector
/// is broken (or a later refactor quietly hollowed it out) is worse than no test, since a green run
/// is read as "no seam exists".
/// </para>
/// </summary>
public class AuthenticationSeamAbsenceTests
{
    private static readonly string[] ScannedRelativeRoots =
    [
        "backend/src",
        "web/src",
        "infra",
        ".github/workflows",
    ];

    [Fact]
    public void No_configuration_key_environment_variable_or_code_path_skips_mints_or_reads_an_authentication_credential_for_test_purposes()
    {
        var repoRoot = FindRepoRoot();
        var violations = AuthenticationSeamScanner.ScanRoots(repoRoot, ScannedRelativeRoots);

        Assert.True(
            violations.Count == 0,
            "[S-T23 / ADR-025 §J.8 / NW-58r] A repository-wide search over backend/src, web/src, " +
            "infra and .github/workflows must find no configuration key, environment variable or " +
            "code path that skips, mints or reads an authentication credential for test purposes " +
            "— ADR-025 §J.8a: no test-only authentication seam ships, on any environment, in any " +
            "wave. Read ADR-025 §J.8 before deleting this test; the fix is removing the seam it " +
            "found, not the assertion:\n" +
            string.Join("\n", violations.Select(v => v.ToString())));
    }

    private static string FindRepoRoot()
    {
        var assemblyLocation = typeof(AuthenticationSeamAbsenceTests).Assembly.Location;
        var dir = new DirectoryInfo(Path.GetDirectoryName(assemblyLocation)!);

        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "backend", "Raffa.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName
            ?? throw new InvalidOperationException(
                "Could not find repo root (looking for backend/Raffa.slnx). " +
                $"Started from: {assemblyLocation}");
    }
}

/// <summary>
/// Proves <see cref="AuthenticationSeamScanner"/> is not vacuously true. Mirrors why
/// <see cref="ArchitectureRuleEnforcementTests"/> exists beside <see cref="DependencyDirectionTests"/>.
/// Exercises <see cref="AuthenticationSeamScanner.ScanText"/> and
/// <see cref="AuthenticationSeamScanner.EnumerateScannableFiles"/> directly against synthetic
/// content — no real product file is touched or needs to be.
/// </summary>
public class AuthenticationSeamAbsenceSelfTests
{
    [Theory]
    [InlineData(
        "if (config[\"SKIP_AUTH_FOR_TESTS\"] == \"true\") { return next(); }",
        "a key that disables authentication")]
    [InlineData(
        "const disableAuthentication = process.env.RAFFA_TEST_MODE === \"1\";",
        "a key that disables authentication")]
    [InlineData(
        "private const string TestPasscode = \"000000\"; // returned instead of the emailed OTP",
        "a path that returns a fixed passcode or token")]
    [InlineData(
        "var fakeToken = \"eyJhbGciOiJub25lIn0\"; return fakeToken;",
        "a path that returns a fixed passcode or token")]
    [InlineData(
        "if (env.EnvironmentName == \"e2e\") { return SkipAuthCheck(context); }",
        "a branch keyed on an environment name that skips a credential check")]
    public void Flags_each_named_ADR_025_section_J_8_shape(string bypassSnippet, string expectedShape)
    {
        var violations = AuthenticationSeamScanner.ScanText(bypassSnippet);

        Assert.Contains(violations, v => v.Shape == expectedShape);
    }

    // Every line below is lifted verbatim (or closely paraphrased) from real, reviewed
    // authentication code in this repository (CallerContext.cs, AzureAdOptions.cs, Program.cs,
    // appsettings.Development.json, identity/main.tf's guest_provisioning_enabled), so this is also
    // a regression guard: if a future edit to the patterns starts flagging the shipped auth stack,
    // it is the patterns that are wrong, not the auth stack.
    [Theory]
    [InlineData("ValidateAudience = true, ValidAudience = azureAdOptions.ClientId,")]
    [InlineData("// the tempting repair (ValidateAudience = false) is forbidden and is exactly what S-T18 asserts against")]
    [InlineData("public string? Authority { get; set; }")]
    [InlineData("guest_provisioning_enabled = var.guest_provisioning_enabled")]
    [InlineData("\"IdentityWorkspace\": \"Host=localhost;Port=5432;Database=raffa_dev;Username=raffa;Password=raffa\"")]
    [InlineData("an integration test validates the token flow end to end, never mocking the credential")]
    public void Does_not_flag_legitimate_authentication_code_or_ordinary_test_prose(string cleanSnippet)
    {
        var violations = AuthenticationSeamScanner.ScanText(cleanSnippet);

        Assert.Empty(violations);
    }

    [Fact]
    public void Repository_scan_skips_build_output_and_dependency_directories()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"authseam-scan-{Guid.NewGuid():N}");
        var keepDir = Path.Combine(tempRoot, "src");
        var binDir = Path.Combine(tempRoot, "src", "bin");
        Directory.CreateDirectory(keepDir);
        Directory.CreateDirectory(binDir);

        try
        {
            File.WriteAllText(Path.Combine(keepDir, "Keep.cs"), "// nothing interesting here");
            // A build's own output is exactly where a copy of a forbidden string could resurface
            // (a source generator, an embedded resource) — the walk must never descend into it.
            File.WriteAllText(Path.Combine(binDir, "Generated.cs"), "SKIP_AUTH_FOR_TESTS");

            var fileNames = AuthenticationSeamScanner.EnumerateScannableFiles(tempRoot)
                .Select(Path.GetFileName)
                .ToList();

            Assert.Contains("Keep.cs", fileNames);
            Assert.DoesNotContain("Generated.cs", fileNames);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }
}

/// <summary>
/// The scan S-T23 runs: <see cref="Patterns"/> is the complete, deliberately short list — three
/// ADR-025 §J.8a shapes, two word orders each — and every entry exists to be cited in a failure
/// message, not to pad coverage. Shared by <see cref="AuthenticationSeamAbsenceTests"/> (the real
/// repository scan) and <see cref="AuthenticationSeamAbsenceSelfTests"/> (the proof it fires).
/// </summary>
internal static class AuthenticationSeamScanner
{
    internal readonly record struct Violation(string Shape, string File, int LineNumber, string Line)
    {
        public override string ToString() => $"  [{Shape}] {File}:{LineNumber}: {Line}";
    }

    /// <summary>
    /// Directory names never descended into: build output and package caches, never source a wave
    /// would hand-author. Checked case-insensitively against path segments, not just the leaf.
    /// </summary>
    private static readonly string[] ExcludedDirNames =
        ["bin", "obj", "node_modules", ".git", "dist", "coverage", ".vs", ".terraform"];

    /// <summary>
    /// Six regexes, three ADR-025 §J.8a shapes in both word orders. Each requires its two trigger
    /// words to sit within a short, whitespace-free gap (<c>[a-z_-]{0,24}</c> / <c>{0,10}</c>) — one
    /// compact identifier or hyphen/underscore-joined key, never bare co-occurrence across a
    /// sentence — which is what keeps this from flagging prose that discusses the ban itself.
    /// </summary>
    internal static readonly (string Shape, Regex Pattern)[] Patterns =
    [
        ("a key that disables authentication",
            new Regex(
                @"\b(skip|disabl(?:e|ed)|bypass(?:ed)?)[a-z_-]{0,24}(auth(?:entication)?|credential|token|password|login|mfa|passcode|otp)",
                RegexOptions.IgnoreCase)),
        ("a key that disables authentication",
            new Regex(
                @"\b(auth(?:entication)?|credential|token|password|login|mfa|passcode|otp)[a-z_-]{0,24}(skip(?:ped)?|disabl(?:e|ed)|bypass(?:ed)?)",
                RegexOptions.IgnoreCase)),
        ("a path that returns a fixed passcode or token",
            new Regex(
                @"\b(test|e2e|fake|stub|dummy|mock|hardcoded|fixed)[a-z_-]{0,24}(passcode|otp|token|password|credential)",
                RegexOptions.IgnoreCase)),
        ("a path that returns a fixed passcode or token",
            new Regex(
                @"\b(passcode|otp|token|password|credential)[a-z_-]{0,24}(test|e2e|fake|stub|dummy|mock|hardcoded|fixed)",
                RegexOptions.IgnoreCase)),
        ("a branch keyed on an environment name that skips a credential check",
            new Regex(
                @"\b(dev|development|e2e|demo|test|staging|local)\b.{0,60}\b(skip|disabl(?:e|ed)|bypass(?:ed)?)[a-z_-]{0,10}(auth|credential|token|password|login|mfa)",
                RegexOptions.IgnoreCase)),
        ("a branch keyed on an environment name that skips a credential check",
            new Regex(
                @"\b(skip|disabl(?:e|ed)|bypass(?:ed)?)[a-z_-]{0,10}(auth|credential|token|password|login|mfa)\b.{0,60}\b(dev|development|e2e|demo|test|staging|local)\b",
                RegexOptions.IgnoreCase)),
    ];

    internal static List<Violation> ScanRoots(string repoRoot, IEnumerable<string> relativeRoots)
    {
        var violations = new List<Violation>();

        foreach (var relativeRoot in relativeRoots)
        {
            var absoluteRoot = Path.Combine(repoRoot, relativeRoot.Replace('/', Path.DirectorySeparatorChar));
            if (!Directory.Exists(absoluteRoot))
            {
                continue;
            }

            foreach (var file in EnumerateScannableFiles(absoluteRoot))
            {
                var displayPath = Path.GetRelativePath(repoRoot, file).Replace('\\', '/');
                violations.AddRange(ScanFile(file, displayPath));
            }
        }

        return violations;
    }

    internal static List<Violation> ScanFile(string absolutePath, string displayPath)
    {
        string text;
        try
        {
            text = File.ReadAllText(absolutePath);
        }
        catch (IOException)
        {
            // Not a file this scan can read as text (locked mid-build) — nothing to assert.
            return [];
        }
        catch (UnauthorizedAccessException)
        {
            return [];
        }

        return ScanText(text)
            .Select(r => new Violation(r.Shape, displayPath, r.LineNumber, r.Line))
            .ToList();
    }

    /// <summary>
    /// One violation per distinct shape matched on a line — never more than one per shape per line,
    /// so a line tripping both the shape-1 and shape-3 pattern (a same-line environment check is
    /// common) is reported once per shape, not once per underlying regex.
    /// </summary>
    internal static List<(string Shape, int LineNumber, string Line)> ScanText(string text)
    {
        var results = new List<(string Shape, int LineNumber, string Line)>();
        var lines = text.Replace("\r\n", "\n").Split('\n');

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var matchedShapes = new HashSet<string>();

            foreach (var (shape, pattern) in Patterns)
            {
                if (matchedShapes.Contains(shape) || !pattern.IsMatch(line))
                {
                    continue;
                }

                matchedShapes.Add(shape);
                results.Add((shape, i + 1, line.Trim()));
            }
        }

        return results;
    }

    internal static IEnumerable<string> EnumerateScannableFiles(string root)
    {
        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(root, file);
            var directorySegments = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            var isExcluded = directorySegments
                .Take(directorySegments.Length - 1)
                .Any(segment => ExcludedDirNames.Contains(segment, StringComparer.OrdinalIgnoreCase));

            if (!isExcluded)
            {
                yield return file;
            }
        }
    }
}
