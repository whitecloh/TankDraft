using System.IO;
using NUnit.Framework;
using UnityEditor.PackageManager;

namespace SLVR.UIMotion.Tests
{
    public sealed class UiMotionArchitectureTests
    {
        [Test]
        public void Runtime_DoesNotUseGlobalDotweenCleanup()
        {
            PackageInfo package = PackageInfo.FindForAssembly(typeof(UiMotionLifecycle).Assembly);
            Assert.That(package, Is.Not.Null);
            string runtimePath = Path.Combine(package.resolvedPath, "Runtime");
            string[] forbidden = { "DOTween." + "KillAll", "DOTween." + "CompleteAll", "DOTween." + "Clear" };

            foreach (string file in Directory.GetFiles(runtimePath, "*.cs", SearchOption.AllDirectories))
            {
                string source = File.ReadAllText(file);
                foreach (string token in forbidden)
                {
                    Assert.That(source, Does.Not.Contain(token), $"Forbidden global cleanup in {file}");
                }
            }
        }
    }
}
