using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Xunit;
using static Headless.NET.Sdk.Tests.Integrations.DotNetCommand;
using StructuredLoggerSerialization = Microsoft.Build.Logging.StructuredLogger.Serialization;

#nullable enable

namespace Headless.NET.Sdk.Tests.Integrations;

public sealed partial class SdkIntegrationTests
{
    [Fact]
    public async Task should_not_overwrite_existing_editorconfig_when_using_defaults()
    {
        const string ExistingEditorConfig = """
root = true

[*.cs]
indent_size = 2
""";

        await using var project = await ConsumerProject.CreateAsync(
            fixture.PackageVersion,
            fixture.PackageSourceDirectory,
            editorConfigContent: ExistingEditorConfig
        );

        var result = await project.RunDotNetAsync(
            $"build {Quote(project.ProjectFilePath)} -p:RestoreConfigFile={Quote(project.NuGetConfigPath)} -p:RestoreIgnoreFailedSources=true -p:SolutionDir={Quote(project.SolutionDirectory)}"
        );

        Assert.True(result.ExitCode == 0, result.Output);
        Assert.Equal(
            NormalizeLineEndings(ExistingEditorConfig),
            NormalizeLineEndings(
                await File.ReadAllTextAsync(project.EditorConfigPath, TestContext.Current.CancellationToken)
            )
        );
    }

    [Fact]
    public async Task should_copy_editorconfig_when_scaffold_target_invoked_with_editorconfig_selector()
    {
        await using var project = await ConsumerProject.CreateAsync(
            fixture.PackageVersion,
            fixture.PackageSourceDirectory,
            enableEditorConfigCopy: true
        );

        var result = await project.RunDotNetAsync(
            $"build {Quote(project.ProjectFilePath)} -t:HeadlessScaffoldConfigFiles -p:RestoreConfigFile={Quote(project.NuGetConfigPath)} -p:RestoreIgnoreFailedSources=true -p:SolutionDir={Quote(project.SolutionDirectory)}"
        );

        Assert.True(result.ExitCode == 0, result.Output);
        Assert.True(
            File.Exists(project.EditorConfigPath),
            "Expected the scaffold target to create .editorconfig when the selector is set."
        );

        var copiedEditorConfig = await File.ReadAllTextAsync(
            project.EditorConfigPath,
            TestContext.Current.CancellationToken
        );
        Assert.Contains("# Common Settings", copiedEditorConfig, StringComparison.Ordinal);

        // Selecting only .editorconfig must not pull in the other files.
        Assert.False(
            File.Exists(project.CSharpierIgnorePath),
            "Did not expect .csharpierignore for editorconfig-only selector."
        );
        Assert.False(File.Exists(project.GitIgnorePath), "Did not expect .gitignore for editorconfig-only selector.");
        Assert.False(
            File.Exists(project.GitAttributesPath),
            "Did not expect .gitattributes for editorconfig-only selector."
        );
    }

    [Fact]
    public async Task should_copy_default_config_files_when_scaffold_target_invoked_with_master_selector()
    {
        await using var project = await ConsumerProject.CreateAsync(
            fixture.PackageVersion,
            fixture.PackageSourceDirectory,
            enableDefaultConfigFilesCopy: true
        );

        var result = await project.RunDotNetAsync(
            $"build {Quote(project.ProjectFilePath)} -t:HeadlessScaffoldConfigFiles -p:RestoreConfigFile={Quote(project.NuGetConfigPath)} -p:RestoreIgnoreFailedSources=true -p:SolutionDir={Quote(project.SolutionDirectory)}"
        );

        Assert.True(result.ExitCode == 0, result.Output);
        Assert.True(File.Exists(project.EditorConfigPath), "Expected the scaffold target to create .editorconfig.");
        Assert.True(
            File.Exists(project.CSharpierIgnorePath),
            "Expected the scaffold target to create .csharpierignore."
        );
        Assert.True(File.Exists(project.GitIgnorePath), "Expected the scaffold target to create .gitignore.");
        Assert.True(File.Exists(project.GitAttributesPath), "Expected the scaffold target to create .gitattributes.");

        var csharpierIgnore = await File.ReadAllTextAsync(
            project.CSharpierIgnorePath,
            TestContext.Current.CancellationToken
        );
        Assert.Contains("**/*.verified.*", csharpierIgnore, StringComparison.Ordinal);
        Assert.Contains("**/*.received.*", csharpierIgnore, StringComparison.Ordinal);
    }

    [Fact]
    public async Task should_not_write_config_files_on_plain_build()
    {
        // A normal build has no side effects: scaffolding only runs via the explicit target.
        await using var project = await ConsumerProject.CreateAsync(
            fixture.PackageVersion,
            fixture.PackageSourceDirectory
        );

        var result = await project.RunDotNetAsync(
            $"build {Quote(project.ProjectFilePath)} --no-incremental -p:RestoreConfigFile={Quote(project.NuGetConfigPath)} -p:RestoreIgnoreFailedSources=true -p:SolutionDir={Quote(project.SolutionDirectory)}"
        );

        Assert.True(result.ExitCode == 0, result.Output);
        Assert.False(File.Exists(project.EditorConfigPath), "Plain build must not create .editorconfig.");
        Assert.False(File.Exists(project.CSharpierIgnorePath), "Plain build must not create .csharpierignore.");
        Assert.False(File.Exists(project.GitIgnorePath), "Plain build must not create .gitignore.");
        Assert.False(File.Exists(project.GitAttributesPath), "Plain build must not create .gitattributes.");
    }

    [Fact]
    public async Task should_scaffold_config_files_when_target_invoked()
    {
        // With no selector set, the explicit target scaffolds the full default set.
        await using var project = await ConsumerProject.CreateAsync(
            fixture.PackageVersion,
            fixture.PackageSourceDirectory
        );

        var result = await project.RunDotNetAsync(
            $"build {Quote(project.ProjectFilePath)} -t:HeadlessScaffoldConfigFiles --no-incremental -p:RestoreConfigFile={Quote(project.NuGetConfigPath)} -p:RestoreIgnoreFailedSources=true -p:SolutionDir={Quote(project.SolutionDirectory)}"
        );

        Assert.True(result.ExitCode == 0, result.Output);
        Assert.True(File.Exists(project.EditorConfigPath), "Expected the scaffold target to create .editorconfig.");
        Assert.True(
            File.Exists(project.CSharpierIgnorePath),
            "Expected the scaffold target to create .csharpierignore."
        );
        Assert.True(File.Exists(project.GitIgnorePath), "Expected the scaffold target to create .gitignore.");
        Assert.True(File.Exists(project.GitAttributesPath), "Expected the scaffold target to create .gitattributes.");
        Assert.Contains($"Created {project.EditorConfigPath}", result.Output, StringComparison.Ordinal);
        Assert.Contains($"Created {project.CSharpierIgnorePath}", result.Output, StringComparison.Ordinal);
        Assert.Contains($"Created {project.GitIgnorePath}", result.Output, StringComparison.Ordinal);
        Assert.Contains($"Created {project.GitAttributesPath}", result.Output, StringComparison.Ordinal);
        Assert.DoesNotContain($"Skipped {project.EditorConfigPath}", result.Output, StringComparison.Ordinal);
        Assert.DoesNotContain($"Skipped {project.CSharpierIgnorePath}", result.Output, StringComparison.Ordinal);
        Assert.DoesNotContain($"Skipped {project.GitIgnorePath}", result.Output, StringComparison.Ordinal);
        Assert.DoesNotContain($"Skipped {project.GitAttributesPath}", result.Output, StringComparison.Ordinal);

        var gitignore = await File.ReadAllTextAsync(project.GitIgnorePath, TestContext.Current.CancellationToken);
        Assert.False(string.IsNullOrWhiteSpace(gitignore), "Expected a non-empty scaffolded .gitignore.");
    }

    [Fact]
    public async Task should_not_overwrite_existing_file_when_scaffolding()
    {
        const string Sentinel = "# sentinel-user-owned-gitignore\n";

        await using var project = await ConsumerProject.CreateAsync(
            fixture.PackageVersion,
            fixture.PackageSourceDirectory,
            additionalFiles: new Dictionary<string, string>(StringComparer.Ordinal) { [".gitignore"] = Sentinel }
        );

        var result = await project.RunDotNetAsync(
            $"build {Quote(project.ProjectFilePath)} -t:HeadlessScaffoldConfigFiles --no-incremental -p:RestoreConfigFile={Quote(project.NuGetConfigPath)} -p:RestoreIgnoreFailedSources=true -p:SolutionDir={Quote(project.SolutionDirectory)}"
        );

        Assert.True(result.ExitCode == 0, result.Output);

        // The user's existing file must be preserved verbatim.
        var gitignore = await File.ReadAllTextAsync(project.GitIgnorePath, TestContext.Current.CancellationToken);
        Assert.Equal(NormalizeLineEndings(Sentinel), NormalizeLineEndings(gitignore));
        Assert.Contains($"Skipped {project.GitIgnorePath}", result.Output, StringComparison.Ordinal);
        Assert.DoesNotContain($"Created {project.GitIgnorePath}", result.Output, StringComparison.Ordinal);

        // Files that did not pre-exist are still created.
        Assert.True(
            File.Exists(project.EditorConfigPath),
            "Expected the scaffold target to create the absent .editorconfig."
        );
        Assert.Contains($"Created {project.EditorConfigPath}", result.Output, StringComparison.Ordinal);
        Assert.DoesNotContain($"Skipped {project.EditorConfigPath}", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task should_overwrite_existing_file_when_force_enabled()
    {
        const string Sentinel = "# sentinel-user-owned-gitignore\n";

        await using var project = await ConsumerProject.CreateAsync(
            fixture.PackageVersion,
            fixture.PackageSourceDirectory,
            additionalFiles: new Dictionary<string, string>(StringComparer.Ordinal) { [".gitignore"] = Sentinel }
        );

        var result = await project.RunDotNetAsync(
            $"build {Quote(project.ProjectFilePath)} -t:HeadlessScaffoldConfigFiles --no-incremental -p:HeadlessOverwriteConfigFiles=true -p:RestoreConfigFile={Quote(project.NuGetConfigPath)} -p:RestoreIgnoreFailedSources=true -p:SolutionDir={Quote(project.SolutionDirectory)}"
        );

        Assert.True(result.ExitCode == 0, result.Output);

        // The sentinel must be replaced with the bundled template content.
        var gitignore = await File.ReadAllTextAsync(project.GitIgnorePath, TestContext.Current.CancellationToken);
        Assert.DoesNotContain("sentinel-user-owned-gitignore", gitignore, StringComparison.Ordinal);
        Assert.Contains("*.rsuser", gitignore, StringComparison.Ordinal);
    }
}
