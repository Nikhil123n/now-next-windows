using System.Globalization;
using System.Xml.Linq;

namespace NowNext.Core.Tests.Presentation;

[TestClass]
public sealed class WindowsIntegrationViewContractTests
{
    [TestMethod]
    public void WindowsAndDataActionsAreNamedAndTouchSizedOutsideFocus()
    {
        XDocument document = XDocument.Load(FindRepositoryFile(
            "src",
            "NowNext.App",
            "MainWindow.xaml"));
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        XElement today = FindNamedElement(document, xaml, "TodayScreen");
        XElement focus = FindNamedElement(document, xaml, "FocusScreen");
        XElement settings = FindNamedElement(today, xaml, "WindowsDataSettingsPanel");

        Assert.AreEqual(
            0,
            focus.Descendants().Count(
                element => element.Attribute(xaml + "Name")?.Value
                    == "WindowsDataSettingsPanel"));
        foreach (XElement action in settings.Descendants().Where(
                     element => element.Name.LocalName is "Button" or "CheckBox"))
        {
            Assert.IsNotNull(action.Attributes().SingleOrDefault(
                attribute => attribute.Name.LocalName == "AutomationProperties.Name"));
            double minimumHeight = double.Parse(
                action.Attribute("MinHeight")?.Value ?? "0",
                CultureInfo.InvariantCulture);
            Assert.IsGreaterThanOrEqualTo(44, minimumHeight);
        }
    }

    [TestMethod]
    public void PackagedStartupTaskIsDeclaredDisabledByDefault()
    {
        XDocument document = XDocument.Load(FindRepositoryFile(
            "src",
            "NowNext.App",
            "Package.appxmanifest"));
        XNamespace desktop = "http://schemas.microsoft.com/appx/manifest/desktop/windows10";
        XElement startupTask = Assert.ContainsSingle(
            document.Descendants(desktop + "StartupTask"));

        Assert.AreEqual("NowNextStartupTask", startupTask.Attribute("TaskId")?.Value);
        Assert.AreEqual("false", startupTask.Attribute("Enabled")?.Value);
        Assert.AreEqual("NOW/NEXT", startupTask.Attribute("DisplayName")?.Value);
    }

    [TestMethod]
    public void ResetAndRestoreRequireExplicitConfirmation()
    {
        string source = File.ReadAllText(FindRepositoryFile(
            "src",
            "NowNext.App",
            "MainWindow.xaml.cs"));

        Assert.Contains("Validate and restore", source, StringComparison.Ordinal);
        Assert.Contains("IsPrimaryButtonEnabled = false", source, StringComparison.Ordinal);
        Assert.Contains("Reset all local data", source, StringComparison.Ordinal);
        Assert.Contains("InterruptForSuspensionAsync", source, StringComparison.Ordinal);
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
