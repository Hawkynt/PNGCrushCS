using System;
using System.Buffers.Binary;
using FileFormat.Jpeg;
using FileFormat.Mpo;

namespace FileFormat.Mpo.Tests;

[TestFixture]
public sealed class MpoWriterTests {

  private static byte[] _CreateMinimalJpegBytes(int width = 4, int height = 4) {
    var rgb = new byte[width * height * 3];
    for (var i = 0; i < rgb.Length; ++i)
      rgb[i] = (byte)(i * 7 % 256);

    var jpeg = new JpegFile {
      Width = width,
      Height = height,
      IsGrayscale = false,
      RgbPixelData = rgb,
    };
    return JpegWriter.ToBytes(jpeg);
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => MpoWriter.ToBytes(null!));

  [Test]
  [Category("Unit")]
  public void ToBytes_EmptyImages_ReturnsEmpty() {
    var file = new MpoFile { Images = [] };

    var result = MpoWriter.ToBytes(file);

    Assert.That(result, Is.Empty);
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_SingleImage_StartsWithJpegSoi() {
    var jpeg = _CreateMinimalJpegBytes();
    var file = new MpoFile { Images = [jpeg] };

    var result = MpoWriter.ToBytes(file);

    Assert.That(result[0], Is.EqualTo(0xFF));
    Assert.That(result[1], Is.EqualTo(0xD8));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_SingleImage_ContainsApp2Marker() {
    var jpeg = _CreateMinimalJpegBytes();
    var file = new MpoFile { Images = [jpeg] };

    var result = MpoWriter.ToBytes(file);

    Assert.That(result[2], Is.EqualTo(0xFF));
    Assert.That(result[3], Is.EqualTo(0xE2));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_SingleImage_ContainsMpfIdentifier() {
    var jpeg = _CreateMinimalJpegBytes();
    var file = new MpoFile { Images = [jpeg] };

    var result = MpoWriter.ToBytes(file);

    // MPF\0 should appear after the APP2 marker (FF E2) + length (2 bytes)
    Assert.That(result[6], Is.EqualTo((byte)'M'));
    Assert.That(result[7], Is.EqualTo((byte)'P'));
    Assert.That(result[8], Is.EqualTo((byte)'F'));
    Assert.That(result[9], Is.EqualTo(0x00));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_MultipleImages_ContainsMpfIdentifier() {
    var jpeg1 = _CreateMinimalJpegBytes(4, 4);
    var jpeg2 = _CreateMinimalJpegBytes(8, 8);
    var file = new MpoFile { Images = [jpeg1, jpeg2] };

    var result = MpoWriter.ToBytes(file);

    // Find "MPF\0" in output
    var found = false;
    for (var i = 0; i < result.Length - 3; ++i)
      if (result[i] == (byte)'M' && result[i + 1] == (byte)'P' && result[i + 2] == (byte)'F' && result[i + 3] == 0x00) {
        found = true;
        break;
      }

    Assert.That(found, Is.True, "MPF identifier not found in output");
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_MultipleImages_ContainsBothJpegSois() {
    var jpeg1 = _CreateMinimalJpegBytes(4, 4);
    var jpeg2 = _CreateMinimalJpegBytes(8, 8);
    var file = new MpoFile { Images = [jpeg1, jpeg2] };

    var result = MpoWriter.ToBytes(file);

    // Count SOI markers (FF D8)
    var soiCount = 0;
    for (var i = 0; i < result.Length - 1; ++i)
      if (result[i] == 0xFF && result[i + 1] == 0xD8)
        ++soiCount;

    Assert.That(soiCount, Is.GreaterThanOrEqualTo(2));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_MultipleImages_SizeLargerThanBothInputs() {
    var jpeg1 = _CreateMinimalJpegBytes(4, 4);
    var jpeg2 = _CreateMinimalJpegBytes(8, 8);
    var file = new MpoFile { Images = [jpeg1, jpeg2] };

    var result = MpoWriter.ToBytes(file);

    Assert.That(result.Length, Is.GreaterThan(jpeg1.Length + jpeg2.Length));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ContainsMpByteOrder() {
    var jpeg = _CreateMinimalJpegBytes();
    var file = new MpoFile { Images = [jpeg] };

    var result = MpoWriter.ToBytes(file);

    // After MPF\0, the MP Header starts with byte order "MM" or "II"
    var mpfPos = -1;
    for (var i = 0; i < result.Length - 3; ++i)
      if (result[i] == (byte)'M' && result[i + 1] == (byte)'P' && result[i + 2] == (byte)'F' && result[i + 3] == 0x00) {
        mpfPos = i + 4;
        break;
      }

    Assert.That(mpfPos, Is.GreaterThan(0));
    // Check for "MM" (big-endian)
    Assert.That(result[mpfPos], Is.EqualTo((byte)'M'));
    Assert.That(result[mpfPos + 1], Is.EqualTo((byte)'M'));
  }
}
