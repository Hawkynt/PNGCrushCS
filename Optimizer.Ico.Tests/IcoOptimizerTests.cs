using System;
using System.IO;
using FileFormat.Ico;
using NUnit.Framework;

namespace Optimizer.Ico.Tests;

[TestFixture]
public sealed class IcoOptimizerTests {

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => IcoOptimizer.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var nonExistentFile = new FileInfo(Path.Combine(Path.GetTempPath(), $"nonexistent_{Guid.NewGuid():N}.ico"));
    Assert.Throws<FileNotFoundException>(() => IcoOptimizer.FromFile(nonExistentFile));
  }

  [Test]
  [Category("Unit")]
  public void Constructor_NullFile_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => new IcoOptimizer(null!, []));
  }

  [Test]
  [Category("Unit")]
  public void Constructor_NullBytes_ThrowsArgumentNullException() {
    var icoFile = new IcoFile { Images = [] };
    Assert.Throws<ArgumentNullException>(() => new IcoOptimizer(icoFile, null!));
  }
}
