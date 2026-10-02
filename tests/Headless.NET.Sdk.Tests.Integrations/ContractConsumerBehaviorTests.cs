using Xunit;

namespace Headless.NET.Sdk.Tests.Integrations;

[Collection(nameof(HeadlessSdkPackageCollection))]
public sealed partial class ContractConsumerBehaviorTests(HeadlessSdkPackageFixture fixture)
{
    private readonly HeadlessSdkPackageFixture fixture = fixture;
}
