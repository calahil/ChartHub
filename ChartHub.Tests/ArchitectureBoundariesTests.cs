using System.Text.RegularExpressions;

namespace ChartHub.Tests;

[Trait(ChartHub.Tests.TestInfrastructure.TestCategories.Category, ChartHub.Tests.TestInfrastructure.TestCategories.Unit)]
public sealed class ArchitectureBoundariesTests
{
    private static readonly Regex UsingRegex = new(@"^\s*using\s+(?<namespace>[\w\.]+)\s*;", RegexOptions.Compiled | RegexOptions.Multiline);

    [Fact]
    public void Services_And_Configuration_DoNotDependOn_ViewLayer()
    {
        string repoRoot = GetRepositoryRoot();
        string[] files = Directory
            .EnumerateFiles(Path.Combine(repoRoot, "ChartHub"), "*.cs", SearchOption.AllDirectories)
            .Where(file => file.Contains("/Services/") || file.Contains("/Configuration/"))
            .ToArray();

        Assert.NotEmpty(files);

        foreach (string file in files)
        {
            string normalizedPath = file.Replace('\\', '/');
            string content = File.ReadAllText(file);
            IReadOnlyCollection<string> importedNamespaces = GetImportedNamespaces(content);

            Assert.DoesNotContain("ChartHub.Views", importedNamespaces);
            Assert.DoesNotContain("ChartHub.Controls", importedNamespaces);
        }
    }

    [Fact]
    public void ViewModels_DoNotDependOn_ViewsOrControls()
    {
        string repoRoot = GetRepositoryRoot();
        string[] files = Directory
            .EnumerateFiles(Path.Combine(repoRoot, "ChartHub", "ViewModels"), "*.cs", SearchOption.AllDirectories)
            .ToArray();

        Assert.NotEmpty(files);

        foreach (string file in files)
        {
            string content = File.ReadAllText(file);
            IReadOnlyCollection<string> importedNamespaces = GetImportedNamespaces(content);

            Assert.DoesNotContain("ChartHub.Views", importedNamespaces);
            Assert.DoesNotContain("ChartHub.Controls", importedNamespaces);
        }
    }

    private static IReadOnlyCollection<string> GetImportedNamespaces(string content)
    {
        return UsingRegex
            .Matches(content)
            .Select(match => match.Groups["namespace"].Value)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    private static string GetRepositoryRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null)
        {
            string candidate = Path.Combine(current.FullName, "ChartHub.sln");
            if (File.Exists(candidate))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root from test execution directory.");
    }
}
