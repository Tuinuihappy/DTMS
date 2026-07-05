using System.Text.RegularExpressions;
using DTMS.Iam.Application.Authorization;

namespace DTMS.ArchitectureTests;

/// <summary>
/// Guards the permission catalog (ADR-017). The catalog in
/// <see cref="Permissions"/> is the single source of truth: every endpoint
/// references a definition here (no raw literals), and every code must be
/// seeded so it is grantable. These tests fail the build when any of those
/// invariants regress.
/// </summary>
public class PermissionCatalogTests
{
    // Strict ADR-017 grammar: dtms:<module>:<resource>:<verb> — exactly four
    // lowercase, kebab-case segments (dtms + three more). The source-system
    // scheme (dtms:source:{key}:order:*) is exempt and not in this catalog.
    private static readonly Regex Grammar =
        new(@"^dtms(:[a-z0-9]+(-[a-z0-9]+)*){3}$", RegexOptions.Compiled);

    [Fact]
    public void Catalog_IsNonEmpty()
        => Assert.True(Permissions.All.Count >= 60, $"catalog has only {Permissions.All.Count} codes");

    [Fact]
    public void EveryCode_MatchesGrammar()
    {
        var bad = Permissions.All.Where(p => !Grammar.IsMatch(p.Code)).Select(p => p.Code).ToList();
        Assert.True(bad.Count == 0, "codes violating grammar: " + string.Join(", ", bad));
    }

    [Fact]
    public void EveryCode_IsUnique()
    {
        var dupes = Permissions.All.GroupBy(p => p.Code).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        Assert.True(dupes.Count == 0, "duplicate codes: " + string.Join(", ", dupes));
    }

    // The modules a code's 2nd segment may name. Guarantees a
    // dtms:<module>:* wildcard grant groups a whole module — the property
    // the ADR-017 rename exists to enable.
    private static readonly HashSet<string> KnownModules = new(StringComparer.Ordinal)
    {
        "dispatch", "deliveryorder", "fleet", "facility", "planning", "iam", "operator", "reporting",
    };

    [Fact]
    public void EveryCode_IsGroupableUnderAKnownModule()
    {
        var bad = Permissions.All
            .Where(p => !KnownModules.Contains(p.Code.Split(':')[1]))
            .Select(p => p.Code).ToList();
        Assert.True(bad.Count == 0,
            "codes whose module segment is not a known module (breaks dtms:<module>:* grouping): "
            + string.Join(", ", bad));
    }

    [Fact]
    public void EveryCode_HasDescriptionAndModule()
    {
        var bad = Permissions.All
            .Where(p => string.IsNullOrWhiteSpace(p.Description) || string.IsNullOrWhiteSpace(p.Module))
            .Select(p => p.Code).ToList();
        Assert.True(bad.Count == 0, "codes missing description/module: " + string.Join(", ", bad));
    }

    /// <summary>
    /// No endpoint may hard-code a permission string — it must reference the
    /// catalog so typos become compile errors. Scans every *.Presentation
    /// source file for a raw <c>RequirePermission("dtms:…")</c> literal. The
    /// source-system path uses RequirePermissionForSourceSystem/FromRouteKey
    /// with StandardSystemPermissions constants and is unaffected.
    /// </summary>
    [Fact]
    public void NoEndpoint_UsesRawPermissionLiteral()
    {
        var literal = new Regex(@"\bRequirePermission\(\s*""dtms:", RegexOptions.Compiled);
        var offenders = new List<string>();
        foreach (var file in EnumeratePresentationSources())
        {
            var text = File.ReadAllText(file);
            if (literal.IsMatch(text))
                offenders.Add(Path.GetFileName(file));
        }
        Assert.True(offenders.Count == 0,
            "raw RequirePermission(\"dtms:…\") literals found in: " + string.Join(", ", offenders));
    }

    /// <summary>
    /// Every catalog code must appear in an IAM seed migration, otherwise the
    /// permission cannot be granted to any role and every endpoint using it
    /// 403s silently. Forward direction only (catalog ⊆ seeded).
    /// </summary>
    [Fact]
    public void EveryCatalogCode_IsSeeded()
    {
        var seeded = SeededCodes();
        var missing = Permissions.All.Select(p => p.Code).Where(c => !seeded.Contains(c)).ToList();
        Assert.True(missing.Count == 0, "catalog codes not seeded in any migration: " + string.Join(", ", missing));
    }

    // ── helpers ──────────────────────────────────────────────────────────

    private static IEnumerable<string> EnumeratePresentationSources()
    {
        var modules = Path.Combine(RepoRoot(), "src", "Modules");
        return Directory.EnumerateDirectories(modules, "*.Presentation", SearchOption.AllDirectories)
            .SelectMany(d => Directory.EnumerateFiles(d, "*.cs", SearchOption.AllDirectories));
    }

    private static HashSet<string> SeededCodes()
    {
        var migrations = Path.Combine(RepoRoot(),
            "src", "Modules", "Iam", "DTMS.Iam.Infrastructure", "Migrations");
        // Codes appear either as SQL string literals ('dtms:…', the seed
        // migrations) or as C# tuple literals ("dtms:…", the rename migration
        // that generates its SQL at runtime). Accept both quote styles.
        var codeRx = new Regex(@"[""'](dtms:[a-z0-9:_-]+)[""']", RegexOptions.Compiled);
        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(migrations, "*.cs"))
            foreach (Match m in codeRx.Matches(File.ReadAllText(file)))
                set.Add(m.Groups[1].Value);
        return set;
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "dtms.slnx")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("repo root (dtms.slnx) not found");
    }
}
