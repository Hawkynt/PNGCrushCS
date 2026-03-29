using System;
using System.IO;
using FileFormat.WebP;

namespace Optimizer.WebP.Tests;

[TestFixture]
public sealed class WebPOptimizerTests {

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => WebPOptimizer.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".webp"));
    Assert.Throws<FileNotFoundException>(() => WebPOptimizer.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void Constructor_NullFile_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => new WebPOptimizer(null!));
  }
}
