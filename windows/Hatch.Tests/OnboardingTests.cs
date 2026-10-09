using System.Text.Json;
using Hatch.Tests.Infrastructure;

namespace Hatch.Tests;

[TestClass]
public sealed class OnboardingTests
{
    [TestMethod]
    public void GetStarted_PersistsConsentAndDoesNotRequireAnAccount()
    {
        if (Environment.GetEnvironmentVariable("HATCH_TEST_ONBOARDING") != "1")
            Assert.Inconclusive("Run the onboarding profile to start with first-run state.");
        TaskWorkflowTests.Find("Onboarding_GetStarted").AsButton().Invoke();
        TaskWorkflowTests.Find("Nav_AllTasks");
        TestSetup.Restart();
        TaskWorkflowTests.Find("Nav_AllTasks");
        Assert.IsNull(TestSetup.MainWindow!.FindFirstDescendant(cf => cf.ByAutomationId("Onboarding_GetStarted")));
        using var settings = JsonDocument.Parse(File.ReadAllText(Path.Combine(TestSetup.DataDirectory, "settings.json")));
        Assert.IsTrue(settings.RootElement.GetProperty("FirstRunComplete").GetBoolean());
        Assert.AreEqual(JsonValueKind.Null, settings.RootElement.GetProperty("SyncAccessToken").ValueKind);
        TaskWorkflowTests.CaptureState("onboarding-complete");
    }
}
