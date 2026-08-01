using System;
using System.Text;
using FileFormat.Core;

namespace FileFormat.IffRgb8.Tests;

/// <summary>
/// RGB8: twenty-five bitplanes and compression four, which is a run per colour rather than per byte.
/// </summary>
[TestFixture]
public sealed class IffRgb8Tests {

  private static RawImage _Bands(int width, int height) {
    var pixels = new byte[width * height * 3];
    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x) {
      var at = (y * width + x) * 3;
      pixels[at] = (byte)(x / 16 * 16);
      pixels[at + 1] = (byte)(y / 16 * 16);
      pixels[at + 2] = 128;
    }

    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = pixels };
  }

  [Test]
  [Category("Unit")]
  public void Written_DeclaresTheBitplanesAndCompressionTheFormatRequires() {
    var bytes = IffRgb8Writer.ToBytes(IffRgb8File.FromRawImage(_Bands(64, 32)));

    Assert.Multiple(() => {
      Assert.That(Encoding.ASCII.GetString(bytes, 0, 4), Is.EqualTo("FORM"));
      Assert.That(Encoding.ASCII.GetString(bytes, 8, 4), Is.EqualTo("RGB8"));
      Assert.That(Encoding.ASCII.GetString(bytes, 12, 4), Is.EqualTo("BMHD"));
      Assert.That(bytes[0x1C], Is.EqualTo(25), "twenty-five bitplanes");
      Assert.That(bytes[0x1E], Is.EqualTo(4), "compression four, not ByteRun1");
    });
  }

  [Test]
  [Category("Unit")]
  public void Runs_CoverLongStretchesInOneUnit() {
    var flat = new RawImage {
      Width = 64,
      Height = 32,
      Format = PixelFormat.Rgb24,
      PixelData = new byte[64 * 32 * 3],
    };

    // One colour throughout: 2048 pixels, which does not fit the seven-bit field.
    var bytes = IffRgb8Writer.ToBytes(IffRgb8File.FromRawImage(flat));
    Assert.That(bytes, Has.Length.LessThan(100), "a flat picture should take a handful of bytes");
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_KeepsEveryPixel() {
    var original = _Bands(64, 32);
    var restored = IffRgb8File.ToRawImage(
      IffRgb8Reader.FromBytes(IffRgb8Writer.ToBytes(IffRgb8File.FromRawImage(original))));

    Assert.Multiple(() => {
      Assert.That(restored.Width, Is.EqualTo(64));
      Assert.That(restored.Height, Is.EqualTo(32));
      Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
    });
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_SurvivesARunLongerThanSixteenBitsWouldNeed() {
    var original = new RawImage {
      Width = 256,
      Height = 256,
      Format = PixelFormat.Rgb24,
      PixelData = new byte[256 * 256 * 3],
    };
    Array.Fill(original.PixelData, (byte)7);

    var restored = IffRgb8File.ToRawImage(
      IffRgb8Reader.FromBytes(IffRgb8Writer.ToBytes(IffRgb8File.FromRawImage(original))));

    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }
}
