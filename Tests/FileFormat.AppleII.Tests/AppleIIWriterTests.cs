using System;
using FileFormat.AppleII;

namespace FileFormat.AppleII.Tests;

[TestFixture]
public sealed class AppleIIWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => AppleIIWriter.ToBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_HgrSize8192() {
    var file = new AppleIIFile {
      Width = 280,
      Height = 192,
      Mode = AppleIIMode.Hgr,
      PixelData = new byte[192 * 40]
    };

    var bytes = AppleIIWriter.ToBytes(file);

    Assert.That(bytes.Length, Is.EqualTo(8192));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_DhgrSize16384() {
    var file = new AppleIIFile {
      Width = 560,
      Height = 192,
      Mode = AppleIIMode.Dhgr,
      PixelData = new byte[192 * 80]
    };

    var bytes = AppleIIWriter.ToBytes(file);

    Assert.That(bytes.Length, Is.EqualTo(16384));
  }
}
