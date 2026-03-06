using System;
using System.IO;
using FileFormat.Ani;

namespace Optimizer.Ani.Tests;

[TestFixture]
public sealed class AniOptimizerTests {

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => AniOptimizer.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var nonExistentFile = new FileInfo(Path.Combine(Path.GetTempPath(), $"nonexistent_{Guid.NewGuid():N}.ani"));
    Assert.Throws<FileNotFoundException>(() => AniOptimizer.FromFile(nonExistentFile));
  }

  [Test]
  [Category("Unit")]
  public void Constructor_NullFile_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => new AniOptimizer(null!, []));
  }
}
