using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Xunit;
using static Headless.NET.Sdk.Tests.Integrations.DotNetCommand;

namespace Headless.NET.Sdk.Tests.Integrations;

public sealed partial class ContractConsumerBehaviorTests
{
    [Fact]
    public async Task should_restore_and_evaluate_a_clean_consumer_with_static_graph()
    {
        await using var project = await ConsumerProject.CreateAsync(
            fixture.PackageVersion,
            fixture.PackageSourceDirectory,
            targetFramework: "net10.0",
            includePackageReference: false
        );
        await WriteProjectAsync(
            project,
            $$"""
            <Project Sdk="Headless.NET.Sdk/{{fixture.PackageVersion}}">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <Nullable>enable</Nullable>
              </PropertyGroup>
              <Target Name="WriteHeadlessStaticGraphContract">
                <PropertyGroup>
                  <_HeadlessContractPackageReferences>@(PackageReference->'%(Identity)', '|')</_HeadlessContractPackageReferences>
                  <_HeadlessContractEditorConfigFiles>@(EditorConfigFiles, '|')</_HeadlessContractEditorConfigFiles>
                </PropertyGroup>
                <Error
                  Condition="'$(HeadlessSdkName)' != 'Headless.NET.Sdk'"
                  Text="Static-graph evaluation lost the Headless SDK identity."
                />
                <WriteLinesToFile
                  File="$(MSBuildProjectDirectory)/static-graph-contract.txt"
                  Lines="SdkName=$(HeadlessSdkName)~PackageReferences=$(_HeadlessContractPackageReferences)~EditorConfigFiles=$(_HeadlessContractEditorConfigFiles)"
                  Overwrite="true"
                />
              </Target>
            </Project>
            """,
            new Dictionary<string, string>(StringComparer.Ordinal)
        );

        var result = await project.RunDotNetAsync(
            $"msbuild {Quote(project.ProjectFilePath)} -restore -graphBuild -t:WriteHeadlessStaticGraphContract -p:RestoreUseStaticGraphEvaluation=true -p:RestoreConfigFile={Quote(project.NuGetConfigPath)} -p:RestoreIgnoreFailedSources=true -nologo"
        );

        Assert.True(result.ExitCode == 0, result.Output);
        await AssertQualityContractFileAsync(Path.Combine(project.RootDirectory, "static-graph-contract.txt"));
    }

    [Fact]
    public async Task should_succeed_design_time_build_and_retain_the_headless_contract()
    {
        await using var project = await ConsumerProject.CreateAsync(
            fixture.PackageVersion,
            fixture.PackageSourceDirectory,
            targetFramework: "net10.0",
            includePackageReference: false
        );
        await WriteProjectAsync(
            project,
            $$"""
            <Project Sdk="Headless.NET.Sdk/{{fixture.PackageVersion}}">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <Nullable>enable</Nullable>
              </PropertyGroup>
              <Target Name="WriteHeadlessDesignTimeContract" DependsOnTargets="Compile">
                <PropertyGroup>
                  <_HeadlessContractPackageReferences>@(PackageReference->'%(Identity)', '|')</_HeadlessContractPackageReferences>
                  <_HeadlessContractEditorConfigFiles>@(EditorConfigFiles, '|')</_HeadlessContractEditorConfigFiles>
                </PropertyGroup>
                <Error Condition="'$(DesignTimeBuild)' != 'true'" Text="Expected a design-time build." />
                <Error
                  Condition="'$(HeadlessSdkName)' != 'Headless.NET.Sdk'"
                  Text="Design-time evaluation lost the Headless SDK identity."
                />
                <WriteLinesToFile
                  File="$(MSBuildProjectDirectory)/design-time-contract.txt"
                  Lines="SdkName=$(HeadlessSdkName)~PackageReferences=$(_HeadlessContractPackageReferences)~EditorConfigFiles=$(_HeadlessContractEditorConfigFiles)"
                  Overwrite="true"
                />
              </Target>
            </Project>
            """,
            new Dictionary<string, string>(StringComparer.Ordinal)
        );

        var result = await project.RunDotNetAsync(
            $"msbuild {Quote(project.ProjectFilePath)} -restore -t:WriteHeadlessDesignTimeContract -p:DesignTimeBuild=true -p:BuildingProject=false -p:SkipCompilerExecution=true -p:RestoreConfigFile={Quote(project.NuGetConfigPath)} -p:RestoreIgnoreFailedSources=true -nologo"
        );

        Assert.True(result.ExitCode == 0, result.Output);
        await AssertQualityContractFileAsync(Path.Combine(project.RootDirectory, "design-time-contract.txt"));
    }

    [Fact]
    public async Task should_import_quality_assets_once_in_outer_and_inner_multitargeting_builds()
    {
        await using var project = await ConsumerProject.CreateAsync(
            fixture.PackageVersion,
            fixture.PackageSourceDirectory,
            targetFramework: "net10.0",
            includePackageReference: true
        );
        await WriteProjectAsync(
            project,
            $$"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFrameworks>net8.0;net9.0</TargetFrameworks>
                <Nullable>enable</Nullable>
              </PropertyGroup>
              <ItemGroup>
                <PackageReference Include="Headless.NET.Sdk" Version="{{fixture.PackageVersion}}" PrivateAssets="all" />
              </ItemGroup>
              <Target
                Name="WriteHeadlessOuterBuildContract"
                BeforeTargets="DispatchToInnerBuilds"
                Condition="'$(IsCrossTargetingBuild)' == 'true'"
              >
                <PropertyGroup>
                  <_HeadlessContractPackageReferences>@(PackageReference->'%(Identity)', '|')</_HeadlessContractPackageReferences>
                  <_HeadlessContractEditorConfigFiles>@(EditorConfigFiles, '|')</_HeadlessContractEditorConfigFiles>
                </PropertyGroup>
                <WriteLinesToFile
                  File="$(MSBuildProjectDirectory)/outer-contract.txt"
                  Lines="SdkName=$(HeadlessSdkName)~PackageReferences=$(_HeadlessContractPackageReferences)~EditorConfigFiles=$(_HeadlessContractEditorConfigFiles)"
                  Overwrite="true"
                />
              </Target>
              <Target
                Name="WriteHeadlessInnerBuildContract"
                AfterTargets="Build"
                Condition="'$(IsCrossTargetingBuild)' != 'true'"
              >
                <PropertyGroup>
                  <_HeadlessContractPackageReferences>@(PackageReference->'%(Identity)', '|')</_HeadlessContractPackageReferences>
                  <_HeadlessContractEditorConfigFiles>@(EditorConfigFiles, '|')</_HeadlessContractEditorConfigFiles>
                </PropertyGroup>
                <WriteLinesToFile
                  File="$(MSBuildProjectDirectory)/inner-$(TargetFramework)-contract.txt"
                  Lines="SdkName=$(HeadlessSdkName)~PackageReferences=$(_HeadlessContractPackageReferences)~EditorConfigFiles=$(_HeadlessContractEditorConfigFiles)"
                  Overwrite="true"
                />
              </Target>
            </Project>
            """,
            new Dictionary<string, string>(StringComparer.Ordinal)
        );

        var result = await project.RunDotNetAsync(
            $"build {Quote(project.ProjectFilePath)} --no-incremental -m:1 -p:RestoreConfigFile={Quote(project.NuGetConfigPath)} -p:RestoreIgnoreFailedSources=true"
        );

        Assert.True(result.ExitCode == 0, result.Output);
        await AssertQualityContractFileAsync(Path.Combine(project.RootDirectory, "outer-contract.txt"));
        foreach (var targetFramework in new[] { "net8.0", "net9.0" })
        {
            await AssertQualityContractFileAsync(
                Path.Combine(project.RootDirectory, $"inner-{targetFramework}-contract.txt")
            );
            Assert.True(
                File.Exists(
                    Path.Combine(project.RootDirectory, "bin", "Debug", targetFramework, "ConsumerProject.dll")
                ),
                $"Expected an output assembly for {targetFramework}."
            );
        }
    }

    [Fact]
    public async Task should_import_versioned_project_sdk_assets_once_in_outer_and_inner_builds()
    {
        await using var project = await ConsumerProject.CreateAsync(
            fixture.PackageVersion,
            fixture.PackageSourceDirectory,
            targetFramework: "net10.0",
            includePackageReference: false
        );
        await WriteProjectAsync(
            project,
            $$"""
            <Project Sdk="Headless.NET.Sdk/{{fixture.PackageVersion}}">
              <PropertyGroup>
                <TargetFrameworks>net8.0;net9.0</TargetFrameworks>
              </PropertyGroup>
              <Target Name="WriteOuterContract" BeforeTargets="DispatchToInnerBuilds" Condition="'$(IsCrossTargetingBuild)' == 'true'">
                <PropertyGroup>
                  <_HeadlessContractPackageReferences>@(PackageReference->'%(Identity)', '|')</_HeadlessContractPackageReferences>
                  <_HeadlessContractEditorConfigFiles>@(EditorConfigFiles, '|')</_HeadlessContractEditorConfigFiles>
                </PropertyGroup>
                <WriteLinesToFile File="$(MSBuildProjectDirectory)/versioned-outer-contract.txt" Lines="SdkName=$(HeadlessSdkName)~PackageReferences=$(_HeadlessContractPackageReferences)~EditorConfigFiles=$(_HeadlessContractEditorConfigFiles)" Overwrite="true" />
              </Target>
              <Target Name="WriteInnerContract" AfterTargets="Build" Condition="'$(IsCrossTargetingBuild)' != 'true'">
                <PropertyGroup>
                  <_HeadlessContractPackageReferences>@(PackageReference->'%(Identity)', '|')</_HeadlessContractPackageReferences>
                  <_HeadlessContractEditorConfigFiles>@(EditorConfigFiles, '|')</_HeadlessContractEditorConfigFiles>
                </PropertyGroup>
                <WriteLinesToFile File="$(MSBuildProjectDirectory)/versioned-inner-$(TargetFramework)-contract.txt" Lines="SdkName=$(HeadlessSdkName)~PackageReferences=$(_HeadlessContractPackageReferences)~EditorConfigFiles=$(_HeadlessContractEditorConfigFiles)" Overwrite="true" />
              </Target>
            </Project>
            """,
            new Dictionary<string, string>(StringComparer.Ordinal)
        );

        var result = await project.RunDotNetAsync(
            $"build {Quote(project.ProjectFilePath)} --no-incremental -m:1 -p:RestoreConfigFile={Quote(project.NuGetConfigPath)} -p:RestoreIgnoreFailedSources=true"
        );

        Assert.True(result.ExitCode == 0, result.Output);
        await AssertQualityContractFileAsync(Path.Combine(project.RootDirectory, "versioned-outer-contract.txt"));
        foreach (var targetFramework in new[] { "net8.0", "net9.0" })
        {
            await AssertQualityContractFileAsync(
                Path.Combine(project.RootDirectory, $"versioned-inner-{targetFramework}-contract.txt")
            );
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task should_retain_package_reference_contract_in_static_graph_and_design_time_evaluation(
        bool designTimeBuild
    )
    {
        await using var project = await ConsumerProject.CreateAsync(
            fixture.PackageVersion,
            fixture.PackageSourceDirectory,
            targetFramework: "net10.0",
            includePackageReference: false
        );
        var contractName = designTimeBuild ? "package-design-time-contract.txt" : "package-static-graph-contract.txt";
        await WriteProjectAsync(
            project,
            $$"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
              </PropertyGroup>
              <ItemGroup>
                <PackageReference Include="Headless.NET.Sdk" Version="{{fixture.PackageVersion}}" PrivateAssets="all" />
              </ItemGroup>
              <Target Name="WritePackageReferenceEvaluationContract">
                <PropertyGroup>
                  <_HeadlessContractPackageReferences>@(PackageReference->'%(Identity)', '|')</_HeadlessContractPackageReferences>
                  <_HeadlessContractEditorConfigFiles>@(EditorConfigFiles, '|')</_HeadlessContractEditorConfigFiles>
                </PropertyGroup>
                <Error Condition="'$(HeadlessSdkName)' != 'Headless.NET.Sdk'" Text="PackageReference evaluation lost the Headless SDK identity." />
                <WriteLinesToFile File="$(MSBuildProjectDirectory)/{{contractName}}" Lines="SdkName=$(HeadlessSdkName)~PackageReferences=$(_HeadlessContractPackageReferences)~EditorConfigFiles=$(_HeadlessContractEditorConfigFiles)" Overwrite="true" />
              </Target>
            </Project>
            """,
            new Dictionary<string, string>(StringComparer.Ordinal)
        );

        var modeArguments = designTimeBuild
            ? "-p:DesignTimeBuild=true -p:BuildingProject=false -p:SkipCompilerExecution=true"
            : "-graphBuild -p:RestoreUseStaticGraphEvaluation=true";
        var result = await project.RunDotNetAsync(
            $"msbuild {Quote(project.ProjectFilePath)} -restore -t:WritePackageReferenceEvaluationContract {modeArguments} -p:RestoreConfigFile={Quote(project.NuGetConfigPath)} -p:RestoreIgnoreFailedSources=true -nologo"
        );

        Assert.True(result.ExitCode == 0, result.Output);
        await AssertQualityContractFileAsync(Path.Combine(project.RootDirectory, contractName));
    }

    [Fact]
    public async Task should_apply_strict_system_text_json_defaults_only_to_compatible_inner_builds()
    {
        await using var project = await ConsumerProject.CreateAsync(
            fixture.PackageVersion,
            fixture.PackageSourceDirectory,
            targetFramework: "net10.0",
            includePackageReference: false
        );
        await WriteProjectAsync(
            project,
            $$"""
            <Project Sdk="Headless.NET.Sdk/{{fixture.PackageVersion}}">
              <PropertyGroup>
                <TargetFrameworks>net8.0;net9.0</TargetFrameworks>
                <HeadlessEnableStrictSystemTextJsonRuntimeDefaults>true</HeadlessEnableStrictSystemTextJsonRuntimeDefaults>
              </PropertyGroup>
              <Target Name="WriteStrictJsonContract" AfterTargets="Build" Condition="'$(IsCrossTargetingBuild)' != 'true'">
                <WriteLinesToFile File="$(MSBuildProjectDirectory)/strict-json-$(TargetFramework)-contract.txt" Lines="@(RuntimeHostConfigurationOption->'%(Identity)=%(Value)', '|')" Overwrite="true" />
              </Target>
            </Project>
            """,
            new Dictionary<string, string>(StringComparer.Ordinal)
        );

        var result = await project.RunDotNetAsync(
            $"build {Quote(project.ProjectFilePath)} --no-incremental -m:1 -p:RestoreConfigFile={Quote(project.NuGetConfigPath)} -p:RestoreIgnoreFailedSources=true"
        );

        Assert.True(result.ExitCode == 0, result.Output);
        var net8Contract = await File.ReadAllTextAsync(
            Path.Combine(project.RootDirectory, "strict-json-net8.0-contract.txt"),
            TestContext.Current.CancellationToken
        );
        Assert.DoesNotContain(
            "System.Text.Json.Serialization.RespectRequiredConstructorParametersDefault",
            net8Contract,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain(
            "System.Text.Json.Serialization.RespectNullableAnnotationsDefault",
            net8Contract,
            StringComparison.Ordinal
        );

        var net9Contract = await File.ReadAllTextAsync(
            Path.Combine(project.RootDirectory, "strict-json-net9.0-contract.txt"),
            TestContext.Current.CancellationToken
        );
        Assert.Contains(
            "System.Text.Json.Serialization.RespectRequiredConstructorParametersDefault=true",
            net9Contract,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "System.Text.Json.Serialization.RespectNullableAnnotationsDefault=true",
            net9Contract,
            StringComparison.Ordinal
        );
    }
}
