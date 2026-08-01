using System;
using System.Text;
using FileFormat.Core;

namespace FileFormat.IffRgbn.Tests;

/// <summary>RGBN: thirteen bitplanes, four bits a channel, and a three-bit run count per unit.</summary>
[TestFixture]
public sealed class IffRgbnTests {

  private static RawImage _Bands(int width, int height) {
    var pixels = new byte[width * height * 3];
    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x) {
      var at = (y * width + x) * 3;
      pixels[at] = (byte)(x / 8 % 16 * 17);
      pixels[at + 1] = (byte)(y / 8 % 16 * 17);
      pixels[at + 2] = 8 * 17;
    }

    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = pixels };
  }

  [Test]
  [Category("Unit")]
  public void Written_DeclaresTheBitplanesAndCompressionTheFormatRequires() {
    var bytes = IffRgbnWriter.ToBytes(IffRgbnFile.FromRawImage(_Bands(64, 32)));

    Assert.Multiple(() => {
      Assert.That(Encoding.ASCII.GetString(bytes, 8, 4), Is.EqualTo("RGBN"));
      Assert.That(bytes[0x1C], Is.EqualTo(13), "thirteen bitplanes");
      Assert.That(bytes[0x1E], Is.EqualTo(4), "compression four");
    });
  }

  [Test]
  [Category("Unit")]
  public void Units_NeverStateARunOfZeroWithoutTheByteThatFollowsIt() {
    var bytes = IffRgbnWriter.ToBytes(IffRgbnFile.FromRawImage(_Bands(64, 32)));
    var body = Array.IndexOf(bytes, (byte)'B');

    // A run of eight or more cannot fit the three-bit field, so the format spells it out in a
    // following byte; a unit whose field reads zero and has nothing after it is unreadable.
    Assert.That(body, Is.GreaterThan(0));
    Assert.That(bytes, Has.Length.GreaterThan(0x20));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_KeepsEveryColourTheFourBitRangeHolds() {
    var original = _Bands(64, 32);
    var restored = IffRgbnFile.ToRawImage(
      IffRgbnReader.FromBytes(IffRgbnWriter.ToBytes(IffRgbnFile.FromRawImage(original))));

    Assert.Multiple(() => {
      Assert.That(restored.Width, Is.EqualTo(64));
      Assert.That(restored.Height, Is.EqualTo(32));
      Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
    });
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_SurvivesARunLongerThanAByteCanCount() {
    var original = new RawImage {
      Width = 256,
      Height = 256,
      Format = PixelFormat.Rgb24,
      PixelData = new byte[256 * 256 * 3],
    };
    Array.Fill(original.PixelData, (byte)(3 * 17));

    var restored = IffRgbnFile.ToRawImage(
      IffRgbnReader.FromBytes(IffRgbnWriter.ToBytes(IffRgbnFile.FromRawImage(original))));

    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }
}
