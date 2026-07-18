using System.Reflection;

using Microsoft.VisualStudio.TestTools.UnitTesting;

[assembly: DoNotParallelize]

namespace NowNext.Core.Tests;

[TestClass]
public sealed class CoreAssemblySmokeTests
{
    [TestMethod]
    public void LoadCoreAssemblyReturnsExpectedAssembly()
    {
        var assemblyName = new AssemblyName("NowNext.Core");

        Assembly assembly = Assembly.Load(assemblyName);

        Assert.AreEqual("NowNext.Core", assembly.GetName().Name);
    }
}
