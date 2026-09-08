using System;
using System.IO;

namespace Hawkynt.FileFormats.Images.Tests;

[SetUpFixture]
public sealed class ReadmeRegenerationHarness {

  private const string _UPDATE_VARIABLE = "UPDATE_IMAGE_FORMAT_README";

  [OneTimeSetUp]
  public void EnableRegeneration()
    => Environment.SetEnvironmentVariable(_UPDATE_VARIABLE, "1");

  [OneTimeTearDown]
  public void CaptureGeneratedReadme() {
    Environment.SetEnvironmentVariable(_UPDATE_VARIABLE, null);

    for (var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
         directory is not null;
         directory = directory.Parent) {
      var readme = Path.Combine(directory.FullName, "Hawkynt.FileFormats.Images", "README.md");
      if (!File.Exists(readme))
        continue;

      var results = Path.Combine(directory.FullName, "Tests", "Hawkynt.FileFormats.Images.Tests", "TestResults");
      Directory.CreateDirectory(results);
      File.Copy(readme, Path.Combine(results, "generated-readme.md"), true);
      return;
    }

    Assert.Fail("Could not locate Hawkynt.FileFormats.Images/README.md for regeneration capture.");
  }
}
