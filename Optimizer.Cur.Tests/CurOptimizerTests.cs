using System;
using System.IO;
using FileFormat.Cur;
using NUnit.Framework;

namespace Optimizer.Cur.Tests;

[TestFixture]
public sealed class CurOptimizerTests {

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => CurOptimizer.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var nonExistentFile = new FileInfo(Path.Combine(Path.GetTempPath(), $"nonexistent_{Guid.NewGuid():N}.cur"));
    Assert.Throws<FileNotFoundException>(() => CurOptimizer.FromFile(nonExistentFile));
  }

  [Test]
  [Category("Unit")]
  public void Constructor_NullFile_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => new CurOptimizer(null!, []));
  }

  [Test]
  [Category("Unit")]
  public void Constructor_NullBytes_ThrowsArgumentNullException() {
    var curFile = new CurFile { Images = [] };
    Assert.Throws<ArgumentNullException>(() => new CurOptimizer(curFile, null!));
  }
}
