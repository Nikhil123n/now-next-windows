using System.Globalization;
using System.Xml.Linq;

namespace NowNext.Core.Tests.Presentation;

[TestClass]
public sealed class WorkdayViewContractTests
{
    private static readonly string[] ExpectedRecoveryChoices =
    [
        "RecoveryResumeButton",
        "RecoveryIncludeButton",
        "RecoveryRebuildButton",
        "RecoveryEndButton",
        "RecoveryCloseEarlyButton",
    ];

    [TestMethod]
    public void TodayShowsOneCalmRepairCalloutAndTouchSizedDayActions()
    {
        XDocument document = LoadXaml();
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        XElement today = FindNamedElement(document, xaml, "TodayScreen");
        XElement callout = FindNamedElement(today, xaml, "RepairCallout");
        XElement controls = FindNamedElement(today, xaml, "DayControls");

        Assert.HasCount(
            1,
            today.Descendants().Where(
                element => element.Attribute(xaml + "Name")?.Value == "RepairCallout"));
        Assert.AreEqual("Collapsed", callout.Attribute("Visibility")?.Value);
        Assert.DoesNotContain("red", callout.ToString(), StringComparison.OrdinalIgnoreCase);
        foreach (XElement button in controls.Descendants().Where(
                     element => element.Name.LocalName == "Button"))
        {
            Assert.IsNotNull(button.Attributes().SingleOrDefault(
                attribute => attribute.Name.LocalName == "AutomationProperties.Name"));
            double minHeight = double.Parse(
                button.Attribute("MinHeight")?.Value ?? "0",
                CultureInfo.InvariantCulture);
            Assert.IsGreaterThanOrEqualTo(44, minHeight);
        }
    }

    [TestMethod]
    public void RecoveryPresentsResumeBeforeRebuildEndAndCloseEarly()
    {
        XDocument document = LoadXaml();
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        XElement recovery = FindNamedElement(document, xaml, "RecoveryPanel");
        string[] orderedChoices = recovery.Descendants()
            .Where(element => element.Name.LocalName == "Button")
            .Select(element => element.Attribute(xaml + "Name")?.Value)
            .Where(name => name is not null)
            .Cast<string>()
            .ToArray();

        Assert.AreSequenceEqual(
            ExpectedRecoveryChoices,
            orderedChoices);
        foreach (XElement button in recovery.Descendants().Where(
                     element => element.Name.LocalName == "Button"))
        {
            Assert.IsNotNull(button.Attributes().SingleOrDefault(
                attribute => attribute.Name.LocalName == "AutomationProperties.Name"));
            Assert.AreEqual("44", button.Attribute("MinHeight")?.Value);
        }
    }

    [TestMethod]
    public void ShutdownAndAbsenceContractsRequireExplicitConfirmation()
    {
        string source = File.ReadAllText(FindRepositoryFile(
            "src",
            "NowNext.App",
            "MainWindow.xaml.cs"));

        Assert.Contains("FromMinutes(15)", source, StringComparison.Ordinal);
        Assert.Contains(">= SubstantialAbsenceThreshold", source, StringComparison.Ordinal);
        Assert.Contains("PrimaryButtonText = \"Confirm Shutdown\"", source, StringComparison.Ordinal);
        Assert.Contains("DefaultButton = ContentDialogButton.Close", source, StringComparison.Ordinal);
        Assert.DoesNotContain("overdue", source, StringComparison.OrdinalIgnoreCase);
    }

    private static XDocument LoadXaml()
    {
        return XDocument.Load(FindRepositoryFile("src", "NowNext.App", "MainWindow.xaml"));
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
