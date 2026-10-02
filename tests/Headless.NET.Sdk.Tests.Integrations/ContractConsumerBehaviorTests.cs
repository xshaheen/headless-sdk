using Xunit;

namespace Headless.NET.Sdk.Tests.Integrations;

// Shared helpers for the Contract*Tests classes. xUnit runs the tests of one class serially, so the
// suite is split into one class per area to let the consumer builds run in parallel.
public abstract partial class ContractConsumerBehaviorTests : IClassFixture<HeadlessSdkPackageFixture>;
