using System;
using FileFormat.PabloPaint;

namespace FileFormat.PabloPaint.Tests;

[TestFixture]
public sealed class PabloPaintWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => PabloPaintWriter.ToBytes(null!));

  [Test]
  [Category("Unit")]
  public void ToBytes_OutputIsExactly32000Bytes() {
    var file = new PabloPaintFile { PixelData = new byte[32000] };
    var bytes = PabloPaintWriter.ToBytes(file);
    Assert.That(bytes.Length, Is.EqualTo(32000));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_PreservesPixelData() {
    var pixelData = new byte[32000];
    pixelData[0] = 0xAA;
    pixelData[1] = 0xBB;
    pixelData[31999] = 0xCC;

    var file = new PabloPaintFile { PixelData = pixelData };
    var bytes = PabloPaintWriter.ToBytes(file);

    Assert.Multiple(() => {
      Assert.That(bytes[0], Is.EqualTo(0xAA));
      Assert.That(bytes[1], Is.EqualTo(0xBB));
      Assert.That(bytes[31999], Is.EqualTo(0xCC));
    });
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_EmptyPixelData_ProducesZeros() {
    var file = new PabloPaintFile { PixelData = [] };
    var bytes = PabloPaintWriter.ToBytes(file);

    Assert.Multiple(() => {
      Assert.That(bytes.Length, Is.EqualTo(32000));
      Assert.That(bytes[0], Is.EqualTo(0));
      Assert.That(bytes[31999], Is.EqualTo(0));
    });
  }
}
