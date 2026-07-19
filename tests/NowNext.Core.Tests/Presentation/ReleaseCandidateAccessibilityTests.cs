using System.Xml.Linq;
using NowNext.App.Presentation;

namespace NowNext.Core.Tests.Presentation;

[TestClass]
public sealed class ReleaseCandidateAccessibilityTests
{
    [TestMethod]
    [DataRow(true, false, false)]
    [DataRow(false, false, true)]
    [DataRow(true, true, true)]
    [DataRow(false, true, true)]
    public void TimerColonPolicyHonorsReducedMotion(
        bool currentVisibility,
        bool reducedMotionEnabled,
        bool expectedVisibility)
    {
        bool actual = TimerColonPolicy.GetNextVisibility(
            currentVisibility,
            reducedMotionEnabled);

        Assert.AreEqual(expectedVisibility, actual);
    }

    [TestMethod]
    public void FocusAndBreakExposeDocumentedKeyboardCommands()
    {
        string xamlPath = FindRepositoryFile("src", "NowNext.App", "MainWindow.xaml");
        string codePath = FindRepositoryFile("src", "NowNext.App", "MainWindow.xaml.cs");
        XDocument document = XDocument.Load(xamlPath);
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        XElement focus = FindNamedElement(document, xaml, "FocusScreen");
        XElement breakScreen = FindNamedElement(document, xaml, "BreakScreen");
        string code = File.ReadAllText(codePath);

        Assert.AreEqual("OnFocusKeyDown", focus.Attribute("KeyDown")?.Value);
        Assert.AreEqual("True", focus.Attribute("IsTabStop")?.Value);
        Assert.AreEqual("OnBreakKeyDown", breakScreen.Attribute("KeyDown")?.Value);
        Assert.AreEqual("True", breakScreen.Attribute("IsTabStop")?.Value);
        Assert.Contains("case VirtualKey.Space:", code);
        Assert.Contains("case VirtualKey.F:", code);
        Assert.Contains("case VirtualKey.P:", code);
        Assert.Contains("case VirtualKey.L:", code);
        Assert.Contains("case VirtualKey.O:", code);
        Assert.Contains("case VirtualKey.E:", code);
        Assert.Contains("args.Key == VirtualKey.Escape", code);
        Assert.Contains("await EndBreakAsync();", code);
    }

    [TestMethod]
    public void ReducedMotionChangesOnlyColonVisibilityPolicy()
    {
        string code = File.ReadAllText(
            FindRepositoryFile("src", "NowNext.App", "MainWindow.xaml.cs"));
        int methodStart = code.IndexOf(
            "private void OnColonTimerTick",
            StringComparison.Ordinal);
        int methodEnd = code.IndexOf(
            "private void OnFocusPointerMoved",
            methodStart,
            StringComparison.Ordinal);
        Assert.IsGreaterThanOrEqualTo(0, methodStart);
        Assert.IsGreaterThan(methodStart, methodEnd);
        string colonHandler = code[methodStart..methodEnd];

        Assert.Contains("TimerColonPolicy.GetNextVisibility", colonHandler);
        Assert.Contains("TimerFirstColonText.Opacity", colonHandler);
        Assert.Contains("TimerSecondColonText.Opacity", colonHandler);
        Assert.DoesNotContain("_focusProjectionTimer", colonHandler);
        Assert.DoesNotContain("FocusControls", colonHandler);
    }

    [TestMethod]
    public void PrototypePackageUsesNarrowLocalSigningContract()
    {
        string manifestPath = FindRepositoryFile("src", "NowNext.App", "Package.appxmanifest");
        string buildScript = File.ReadAllText(
            FindRepositoryFile("scripts", "Build-PrototypePackage.ps1"));
        string installScript = File.ReadAllText(
            FindRepositoryFile("scripts", "Install-PrototypePackage.ps1"));
        XDocument manifest = XDocument.Load(manifestPath);
        XNamespace foundation = "http://schemas.microsoft.com/appx/manifest/foundation/windows10";
        XNamespace desktop = "http://schemas.microsoft.com/appx/manifest/desktop/windows10";

        XElement identity = manifest.Root!.Element(foundation + "Identity")!;
        XElement startupExtension = manifest
            .Descendants(desktop + "Extension")
            .Single(element => element.Attribute("Category")?.Value == "windows.startupTask");

        Assert.AreEqual("CN=NowNext Development", identity.Attribute("Publisher")?.Value);
        Assert.AreEqual("NowNext.App.exe", startupExtension.Attribute("Executable")?.Value);
        Assert.Contains("-p:AppxPackageSigningEnabled=true", buildScript);
        Assert.Contains("Export-Certificate", buildScript);
        Assert.Contains("Cert:\\LocalMachine\\TrustedPeople", installScript);
        Assert.Contains("Get-AppxPackage -Name 'NowNext.LocalPrototype'", installScript);
        Assert.DoesNotContain("TrustedRoot", installScript);
        Assert.DoesNotContain("-AllowUnsigned", installScript);
    }

    private static XElement FindNamedElement(
        XContainer container,
        XNamespace xaml,
        string name)
    {
        return container.Descendants().Single(
            element => element.Attribute(xaml + "Name")?.Value == name);
    }

    private static string FindRepositoryFile(params string[] pathSegments)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            string candidate = Path.Combine([directory.FullName, .. pathSegments]);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        Assert.Fail("The repository root could not be located from the test output directory.");
        return string.Empty;
    }
}
