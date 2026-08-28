using System.Text;
using FileFormat.Core;
using FileFormat.Fits;

namespace FileFormat.Fits.Tests;

[TestFixture]
public sealed class ColourCubeTests {

  [Test]
  [Category("Integration")]
  public void Rgb24_RoundTrip_PreservesChannelsAndPixels() {
    var image = new RawImage {
      Width = 3,
      Height = 2,
      Format = PixelFormat.Rgb24,
      PixelData = [
        255, 0, 0,   0, 255, 0,   0, 0, 255,
        10, 20, 30,  40, 50, 60,  70, 80, 90,
      ]
    };

    var file = FitsFile.FromRawImage(image);
    var bytes = FitsWriter.ToBytes(file);
    var parsed = FitsReader.FromBytes(bytes);
    var restored = FitsFile.ToRawImage(parsed);
    var header = Encoding.ASCII.GetString(bytes, 0, 2880);

    Assert.That(file.Channels, Is.EqualTo(3));
    Assert.That(parsed.Channels, Is.EqualTo(3));
    Assert.That(header, Does.Contain("NAXIS3"));
    Assert.That(restored.Format, Is.EqualTo(PixelFormat.Rgb24));
    Assert.That(restored.PixelData, Is.EqualTo(image.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void Rgba32_RoundTrip_PreservesAlphaPlane() {
    var image = new RawImage {
      Width = 2,
      Height = 2,
      Format = PixelFormat.Rgba32,
      PixelData = [
        255, 0, 0, 7,   0, 255, 0, 63,
        0, 0, 255, 127, 255, 255, 255, 251,
      ]
    };

    var parsed = FitsReader.FromBytes(FitsWriter.ToBytes(FitsFile.FromRawImage(image)));
    var restored = FitsFile.ToRawImage(parsed);

    Assert.That(parsed.Channels, Is.EqualTo(4));
    Assert.That(restored.Format, Is.EqualTo(PixelFormat.Rgba32));
    Assert.That(restored.PixelData, Is.EqualTo(image.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void Gray16_Writer_DeclaresStandardUnsignedOffsetAndRoundTripsExactly() {
    var image = new RawImage {
      Width = 4,
      Height = 1,
      Format = PixelFormat.Gray16,
      PixelData = [
        0x00, 0x00,
        0x12, 0x34,
        0x80, 0x00,
        0xFF, 0xFF,
      ]
    };

    var bytes = FitsWriter.ToBytes(FitsFile.FromRawImage(image));
    var restored = FitsFile.ToRawImage(FitsReader.FromBytes(bytes));
    var header = Encoding.ASCII.GetString(bytes, 0, 2880);

    Assert.That(header, Does.Contain("BZERO"));
    Assert.That(header, Does.Contain("32768"));
    Assert.That(restored.Format, Is.EqualTo(PixelFormat.Gray16));
    Assert.That(restored.PixelData, Is.EqualTo(image.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void Rgb48_RoundTrip_PreservesAllSixteenBits() {
    var image = new RawImage {
      Width = 2,
      Height = 1,
      Format = PixelFormat.Rgb48,
      PixelData = [
        0x00, 0x01, 0x12, 0x34, 0xFE, 0xDC,
        0x80, 0x00, 0x7F, 0xFF, 0xFF, 0xFF,
      ]
    };

    var parsed = FitsReader.FromBytes(FitsWriter.ToBytes(FitsFile.FromRawImage(image)));
    var restored = FitsFile.ToRawImage(parsed);

    Assert.That(parsed.Channels, Is.EqualTo(3));
    Assert.That(restored.Format, Is.EqualTo(PixelFormat.Rgb48));
    Assert.That(restored.PixelData, Is.EqualTo(image.PixelData));
  }
}
