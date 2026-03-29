using System;
using FileFormat.DoodleComp;

namespace FileFormat.DoodleComp.Tests;

[TestFixture]
public sealed class DoodleCompWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_NullFile_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => DoodleCompWriter.ToBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_OutputStartsWithLoadAddress() {
    var file = new DoodleCompFile {
      LoadAddress = 0x5C00,
      BitmapData = new byte[8000],
      ScreenRam = new byte[1000],
    };

    var bytes = DoodleCompWriter.ToBytes(file);

    Assert.That(bytes[0], Is.EqualTo(0x00));
    Assert.That(bytes[1], Is.EqualTo(0x5C));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_CompressedOutputSmallerThanRaw() {
    var file = new DoodleCompFile {
      LoadAddress = 0x5C00,
      BitmapData = new byte[8000], // all zeros -> compresses well
      ScreenRam = new byte[1000],  // all zeros -> compresses well
    };

    var bytes = DoodleCompWriter.ToBytes(file);

    // Compressed should be significantly smaller than 2 + 9000
    Assert.That(bytes.Length, Is.LessThan(9002));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_OutputAtLeastMinimumSize() {
    var file = new DoodleCompFile {
      LoadAddress = 0x5C00,
      BitmapData = new byte[8000],
      ScreenRam = new byte[1000],
    };

    var bytes = DoodleCompWriter.ToBytes(file);

    Assert.That(bytes.Length, Is.GreaterThanOrEqualTo(DoodleCompFile.MinimumFileSize));
  }
}
