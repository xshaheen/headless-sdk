using Xunit;

namespace Headless.NET.Sdk.Tests.Integrations;

[Collection(nameof(HeadlessSdkPackageCollection))]
public sealed partial class ContractConsumerBehaviorTests
{
    private readonly HeadlessSdkPackageFixture fixture;

    public ContractConsumerBehaviorTests(HeadlessSdkPackageFixture fixture)
    {
        this.fixture = fixture;
    }
}
