using System.Globalization;
using System.Xml.Linq;

namespace NowNext.Core.Tests.Presentation;

[TestClass]
public sealed class FocusViewContractTests
{
    [TestMethod]
    public void FocusXamlKeepsNormalContentToLabelAndTimerWithCollapsedControlOverlays()
    {
        XDocument document = XDocument.Load(FindRepositoryFile(
            "src",
            "NowNext.App",
            "MainWindow.xaml"));
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        XElement focus = FindNamedElement(document, xaml, "FocusScreen");
        XElement controls = FindNamedElement(focus, xaml, "FocusControls");
        XElement recovery = FindNamedElement(focus, xaml, "RecoveryPanel");
        XElement taskEditor = FindNamedElement(document, xaml, "TaskEditorDialog");

        Assert.IsNotNull(FindNamedElement(focus, xaml, "FocusLabelText"));
        Assert.IsNotNull(FindNamedElement(focus, xaml, "TimerDisplay"));
        Assert.AreEqual("Collapsed", controls.Attribute("Visibility")?.Value);
        Assert.AreEqual("Collapsed", recovery.Attribute("Visibility")?.Value);
        Assert.AreEqual("Collapsed", taskEditor.Attribute("Visibility")?.Value);
        Assert.AreEqual(
            0,
            focus.Descendants().Count(element => element.Name.LocalName == "ProgressBar"));
    }

    [TestMethod]
    public void FocusXamlSeparatesColonGlyphsAndUsesTouchSizedControls()
    {
        XDocument document = XDocument.Load(FindRepositoryFile(
            "src",
            "NowNext.App",
            "MainWindow.xaml"));
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        XElement focus = FindNamedElement(document, xaml, "FocusScreen");
        XElement firstColon = FindNamedElement(focus, xaml, "TimerFirstColonText");
        XElement secondColon = FindNamedElement(focus, xaml, "TimerSecondColonText");

        Assert.AreEqual(":", firstColon.Attribute("Text")?.Value);
        Assert.AreEqual(":", secondColon.Attribute("Text")?.Value);
        foreach (XElement button in focus.Descendants().Where(
                     element => element.Name.LocalName == "Button"))
        {
            double minHeight = double.Parse(
                button.Attribute("MinHeight")?.Value ?? "0",
                CultureInfo.InvariantCulture);
            Assert.IsGreaterThanOrEqualTo(44, minHeight);
        }
    }

    private static XElement FindNamedElement(XContainer container, XNamespace xaml, string name)
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
