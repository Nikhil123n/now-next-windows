using System.Globalization;
using System.Xml.Linq;

namespace NowNext.Core.Tests.Presentation;

[TestClass]
public sealed class BreakViewContractTests
{
    [TestMethod]
    public void BreakSurfaceShowsOnePromptAndKeepsReturnContextInitiallyHidden()
    {
        XDocument document = XDocument.Load(FindRepositoryFile(
            "src",
            "NowNext.App",
            "MainWindow.xaml"));
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        XElement breakScreen = FindNamedElement(document, xaml, "BreakScreen");
        XElement returnContext = FindNamedElement(
            breakScreen,
            xaml,
            "BreakReturnContext");

        Assert.AreEqual("Collapsed", breakScreen.Attribute("Visibility")?.Value);
        Assert.HasCount(
            1,
            breakScreen.Descendants().Where(
                element => element.Attribute(xaml + "Name")?.Value == "BreakPromptText"));
        Assert.AreEqual("Collapsed", returnContext.Attribute("Visibility")?.Value);
        Assert.AreEqual(
            0,
            breakScreen.Descendants().Count(
                element => element.Name.LocalName == "ProgressBar"));
    }

    [TestMethod]
    public void EveryBreakActionHasATouchSizedTarget()
    {
        XDocument document = XDocument.Load(FindRepositoryFile(
            "src",
            "NowNext.App",
            "MainWindow.xaml"));
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        XElement breakScreen = FindNamedElement(document, xaml, "BreakScreen");

        foreach (XElement button in breakScreen.Descendants().Where(
                     element => element.Name.LocalName == "Button"))
        {
            double minHeight = double.Parse(
                button.Attribute("MinHeight")?.Value ?? "0",
                CultureInfo.InvariantCulture);
            Assert.IsGreaterThanOrEqualTo(44, minHeight);
        }
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
