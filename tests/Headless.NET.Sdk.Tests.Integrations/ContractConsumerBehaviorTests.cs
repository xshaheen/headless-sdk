using Xunit;

namespace Headless.NET.Sdk.Tests.Integrations;

// Shared helpers for the Contract*Tests classes, one class per area. Tests run in parallel within a
// class as well (xunit.runner.json), so a class groups by topic, not by schedule.
public abstract partial class ContractConsumerBehaviorTests : IClassFixture<HeadlessSdkPackageFixture>;
