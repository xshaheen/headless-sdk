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

[CollectionDefinition(nameof(HeadlessSdkPackageCollection))]
public sealed class HeadlessSdkPackageCollection : ICollectionFixture<HeadlessSdkPackageFixture>;

[Collection(nameof(HeadlessSdkPackageCollection))]
public sealed partial class SdkIntegrationTests(HeadlessSdkPackageFixture fixture)
{
    private static string NormalizeLineEndings(string value) => value.ReplaceLineEndings("\n");

    private static string CreateCentralPackageManagementProps(string packageVersionItems) =>
        $$"""
            <Project>
              <PropertyGroup>
                <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
                <CentralPackageTransitivePinningEnabled>true</CentralPackageTransitivePinningEnabled>
              </PropertyGroup>
              <ItemGroup>
                {{packageVersionItems}}
              </ItemGroup>
            </Project>
            """;

    private static void AssertImplicitAnalyzerReference(
        IReadOnlyDictionary<string, XElement> packageReferences,
        string packageId
    )
    {
        Assert.True(packageReferences.TryGetValue(packageId, out var packageReference), $"Missing {packageId}.");
        Assert.False(string.IsNullOrWhiteSpace(packageReference.Attribute("Version")?.Value));
        Assert.Equal("true", packageReference.Attribute("IsImplicitlyDefined")?.Value);
        Assert.Equal("all", packageReference.Element("PrivateAssets")?.Value);
        Assert.Contains("analyzers", packageReference.Element("IncludeAssets")?.Value, StringComparison.Ordinal);
    }

    private static bool IsBuildAsset(ZipArchiveEntry entry)
    {
        if (
            !entry.FullName.EndsWith(".props", StringComparison.Ordinal)
            && !entry.FullName.EndsWith(".targets", StringComparison.Ordinal)
        )
        {
            return false;
        }

        return entry.FullName.StartsWith("build/", StringComparison.Ordinal)
            || entry.FullName.StartsWith("buildMultiTargeting/", StringComparison.Ordinal);
    }

    private static string ReadPackageEntry(ZipArchive package, string entryName)
    {
        var entry = package.GetEntry(entryName);
        Assert.NotNull(entry);

        using var reader = new StreamReader(entry.Open());
        return reader.ReadToEnd();
    }

    private static byte[] ReadPackageEntryBytes(ZipArchive package, string entryName)
    {
        var entry = package.GetEntry(entryName);
        Assert.NotNull(entry);

        using var stream = entry.Open();
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    // "MPDB" is the magic header of the embedded portable PDB debug-directory blob (ECMA-335
    // Portable PDB spec); its presence in the image is what "DebugType=embedded" produces.
    private static bool HasEmbeddedPortablePdb(byte[] assembly) => assembly.AsSpan().IndexOf("MPDB"u8) >= 0;
}
