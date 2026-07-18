#!/usr/bin/env bash

set -euo pipefail

repository_root=$(cd "$(dirname "${BASH_SOURCE[0]}")/../../.." && pwd)
packages_directory=${1:?Packed-package directory is required.}
packages_directory=$(cd "$packages_directory" && pwd)
test_root=$(mktemp -d)

cleanup() {
  status=$?
  if (( status == 0 )); then
    rm -rf "$test_root"
  else
    echo "Failed consumer project preserved at $test_root" >&2
  fi
}
trap cleanup EXIT

package_paths=()
for candidate in "$packages_directory"/Headless.NET.Sdk.Test.*.nupkg; do
  if [[ -f $candidate && $candidate != *.snupkg ]]; then
    package_paths+=("$candidate")
  fi
done

if (( ${#package_paths[@]} != 1 )); then
  echo "Expected exactly one Headless.NET.Sdk.Test package; found ${#package_paths[@]}." >&2
  exit 1
fi

package_file=$(basename "${package_paths[0]}")
package_version=${package_file#Headless.NET.Sdk.Test.}
package_version=${package_version%.nupkg}
xunit_version=$(
  sed -nE 's/.*PackageVersion Include="xunit\.v3\.mtp-v2" Version="([^"]+)".*/\1/p' \
    "$repository_root/Directory.Packages.props"
)

if [[ -z $xunit_version ]]; then
  echo "Could not read the xunit.v3.mtp-v2 version from Directory.Packages.props." >&2
  exit 1
fi

mkdir -p "$test_root/local-source" "$test_root/nuget-packages" "$test_root/dotnet-home"
cp "$packages_directory"/*.nupkg "$test_root/local-source/"
cp "$repository_root/global.json" "$test_root/global.json"

cat > "$test_root/NuGet.Config" <<EOF
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="packed-headless-sdk" value="$test_root/local-source" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
  <packageSourceMapping>
    <packageSource key="packed-headless-sdk">
      <package pattern="Headless.NET.Sdk*" />
    </packageSource>
    <packageSource key="nuget.org">
      <package pattern="*" />
    </packageSource>
  </packageSourceMapping>
</configuration>
EOF

cat > "$test_root/ConsumerProject.csproj" <<EOF
<Project Sdk="Headless.NET.Sdk.Test/$package_version">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="xunit.v3.mtp-v2" Version="$xunit_version" />
  </ItemGroup>
</Project>
EOF

cat > "$test_root/ContractSmokeTests.cs" <<'EOF'
using Xunit;

public sealed class ContractSmokeTests
{
    [Fact]
    public void passes() => Assert.True(true);
}
EOF

export DOTNET_CLI_HOME="$test_root/dotnet-home"
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_NOLOGO=1
export DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1
export NUGET_PACKAGES="$test_root/nuget-packages"

(
  cd "$test_root"
  dotnet restore ConsumerProject.csproj \
    --configfile NuGet.Config \
    -p:RestorePackagesWithLockFile=false \
    -p:RestoreLockedMode=false
  dotnet test --project ConsumerProject.csproj --no-restore
)

echo "Clean .NET 10 MTP consumer test passed."
