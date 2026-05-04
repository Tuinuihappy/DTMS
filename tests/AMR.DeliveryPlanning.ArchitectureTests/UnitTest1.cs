namespace AMR.DeliveryPlanning.ArchitectureTests;

public class ModuleBoundaryTests
{
    [Fact]
    public void ModuleInfrastructureProjects_DoNotReference_OtherModuleInfrastructureProjects()
    {
        var repoRoot = FindRepoRoot();
        var moduleProjects = Directory.GetFiles(
            Path.Combine(repoRoot, "src", "Modules"),
            "*.csproj",
            SearchOption.AllDirectories);

        var violations = moduleProjects
            .Where(path => Path.GetFileNameWithoutExtension(path).EndsWith(".Infrastructure", StringComparison.Ordinal))
            .SelectMany(project => FindForbiddenInfrastructureReferences(repoRoot, project))
            .OrderBy(v => v)
            .ToList();

        Assert.True(
            violations.Count == 0,
            "Module Infrastructure projects must not reference another module's Infrastructure project. " +
            "Use Application contracts/read services instead.\n" +
            string.Join('\n', violations));
    }

    [Fact]
    public void VendorAdapterFactory_DoesNotResolveAdapters_ByConcreteTypeNameOrFallback()
    {
        var repoRoot = FindRepoRoot();
        var factoryPath = Path.Combine(
            repoRoot,
            "src",
            "Modules",
            "VendorAdapter",
            "AMR.DeliveryPlanning.VendorAdapter.Infrastructure",
            "Services",
            "VendorAdapterFactory.cs");

        var factoryText = File.ReadAllText(factoryPath);

        Assert.DoesNotContain("GetServices<IVehicleCommandService>", factoryText);
        Assert.DoesNotContain("GetType().Name", factoryText);
        Assert.DoesNotContain("Contains(\"Riot3\"", factoryText);
        Assert.DoesNotContain("fallback", factoryText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CriticalWorkflowCommands_DoNotPublishIntegrationEvents_OutsideModuleOutbox()
    {
        var repoRoot = FindRepoRoot();
        var commandRoots = new[]
        {
            Path.Combine(repoRoot, "src", "Modules", "DeliveryOrder", "AMR.DeliveryPlanning.DeliveryOrder.Application", "Commands"),
            Path.Combine(repoRoot, "src", "Modules", "Planning", "AMR.DeliveryPlanning.Planning.Application", "Commands"),
            Path.Combine(repoRoot, "src", "Modules", "Dispatch", "AMR.DeliveryPlanning.Dispatch.Application", "Commands"),
            Path.Combine(repoRoot, "src", "Modules", "Fleet", "AMR.DeliveryPlanning.Fleet.Application", "Commands"),
            Path.Combine(repoRoot, "src", "Modules", "Fleet", "AMR.DeliveryPlanning.Fleet.Application", "Consumers"),
            Path.Combine(repoRoot, "src", "Modules", "VendorAdapter", "AMR.DeliveryPlanning.VendorAdapter.Feeder", "Webhooks")
        };

        var violations = commandRoots
            .SelectMany(root => Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories))
            .Where(path =>
            {
                var text = File.ReadAllText(path);
                return text.Contains("IEventBus", StringComparison.Ordinal) ||
                       text.Contains(".PublishAsync(", StringComparison.Ordinal);
            })
            .Select(path => Path.GetRelativePath(repoRoot, path))
            .OrderBy(path => path)
            .ToList();

        Assert.True(
            violations.Count == 0,
            "Critical workflow commands must write integration events to their module-owned outbox " +
            "before committing domain data, instead of publishing through IEventBus after SaveChanges.\n" +
            string.Join('\n', violations));
    }

    [Fact]
    public void MigratedCommandHandlers_DoNotUseExplicitModuleOutbox()
    {
        var repoRoot = FindRepoRoot();
        var commandRoots = new[]
        {
            Path.Combine(repoRoot, "src", "Modules", "DeliveryOrder", "AMR.DeliveryPlanning.DeliveryOrder.Application", "Commands"),
            Path.Combine(repoRoot, "src", "Modules", "Planning", "AMR.DeliveryPlanning.Planning.Application", "Commands"),
            Path.Combine(repoRoot, "src", "Modules", "Dispatch", "AMR.DeliveryPlanning.Dispatch.Application", "Commands"),
            Path.Combine(repoRoot, "src", "Modules", "Fleet", "AMR.DeliveryPlanning.Fleet.Application", "Commands")
        };

        var forbiddenTokens = new[]
        {
            "IDeliveryOrderOutbox",
            "IPlanningOutbox",
            "IDispatchOutbox",
            "_outbox.AddAsync(",
            "AddAsync(new DeliveryOrder",
            "AddAsync(new PlanCommitted",
            "AddAsync(new Trip",
            "AddAsync(new ExceptionRaised",
            "AddAsync(new PodCaptured",
            "AddAsync(new VehicleMaintenance"
        };

        var violations = commandRoots
            .SelectMany(root => Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories))
            .Where(path =>
            {
                var text = File.ReadAllText(path);
                return forbiddenTokens.Any(token => text.Contains(token, StringComparison.Ordinal));
            })
            .Select(path => Path.GetRelativePath(repoRoot, path))
            .OrderBy(path => path)
            .ToList();

        Assert.True(
            violations.Count == 0,
            "Migrated command handlers must emit domain events and let the module DbContext interceptor " +
            "write outbox rows in the same transaction. Explicit outbox remains allowed only for documented " +
            "adapter/policy exceptions.\n" +
            string.Join('\n', violations));
    }

    private static IEnumerable<string> FindForbiddenInfrastructureReferences(string repoRoot, string projectPath)
    {
        var projectModule = GetModuleName(projectPath);
        var projectText = File.ReadAllText(projectPath);

        foreach (var line in projectText.Split('\n'))
        {
            if (!line.Contains("<ProjectReference", StringComparison.OrdinalIgnoreCase) ||
                !line.Contains(".Infrastructure", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var includeStart = line.IndexOf("Include=\"", StringComparison.OrdinalIgnoreCase);
            if (includeStart < 0) continue;

            includeStart += "Include=\"".Length;
            var includeEnd = line.IndexOf('"', includeStart);
            if (includeEnd < 0) continue;

            var include = line[includeStart..includeEnd];
            var referencedPath = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(projectPath)!, include));
            var referencedModule = GetModuleName(referencedPath);

            if (!string.Equals(projectModule, referencedModule, StringComparison.OrdinalIgnoreCase))
            {
                return new[]
                {
                    $"{Path.GetRelativePath(repoRoot, projectPath)} -> {Path.GetRelativePath(repoRoot, referencedPath)}"
                };
            }
        }

        return [];
    }

    private static string GetModuleName(string path)
    {
        var parts = path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var moduleIndex = Array.IndexOf(parts, "Modules");
        return moduleIndex >= 0 && moduleIndex + 1 < parts.Length
            ? parts[moduleIndex + 1]
            : string.Empty;
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "src", "Modules")))
                return directory.FullName;

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root from test output directory.");
    }
}
