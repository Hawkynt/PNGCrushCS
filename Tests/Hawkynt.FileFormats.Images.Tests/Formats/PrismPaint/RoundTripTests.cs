using System;
using FileFormat.Core;
using FileFormat.PrismPaint;

namespace FileFormat.PrismPaint.Tests;

/// <summary>
/// Writing a Prism Paint picture and reading it back.
/// </summary>
/// <remarks>
/// UNVERIFIED AGAINST ANY OTHER TOOL. The reader is checked — a real sample matches RECOIL on every
/// pixel — and the writer is aligned with it, so the two agree. That is not the same as being right:
/// RECOIL refuses a file this writes with eight bitplanes, which is what a picture round-tripped
/// through a 256-entry palette becomes, and why is not settled.
/// <para/>
/// These tests therefore say only that the pair is consistent, which is exactly what the old pair
/// said while both were wrong. They are not evidence that the writer is correct.
/// </remarks>
[TestFixture]
public sealed class RoundTripTests {

  private static PrismPaintFile _Picture(int width, int height, int planes) {
    var colors = 1 << planes;
    var palette = new byte[colors * 3];
    for (var i = 0; i < colors; ++i) {
      palette[i * 3] = (byte)(i * 255 / (colors - 1));
      palette[i * 3 + 1] = (byte)(255 - i * 255 / (colors - 1));
      palette[i * 3 + 2] = (byte)(i % 4 * 85);
    }

    var pixels = new byte[width * height];
    for (var i = 0; i < pixels.Length; ++i)
      pixels[i] = (byte)(i % colors);

    return new() { Width = width, Height = height, Palette = palette, PixelData = pixels };
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_KeepsTheSize() {
    var restored = PrismPaintReader.FromBytes(PrismPaintWriter.ToBytes(_Picture(320, 200, 4)));

    Assert.Multiple(() => {
      Assert.That(restored.Width, Is.EqualTo(320));
      Assert.That(restored.Height, Is.EqualTo(200));
    });
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_KeepsEveryIndex() {
    var original = _Picture(320, 200, 4);
    var restored = PrismPaintReader.FromBytes(PrismPaintWriter.ToBytes(original));

    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_KeepsThePaletteToTheScaleItStores() {
    var original = _Picture(64, 32, 4);
    var restored = PrismPaintReader.FromBytes(PrismPaintWriter.ToBytes(original));

    Assert.Multiple(() => {
      for (var i = 0; i < 48; ++i)
        Assert.That(restored.Palette[i], Is.EqualTo(original.Palette[i]).Within(2), $"channel {i}");
    });
  }

  [Test]
  [Category("Integration")]
  public void Written_BeginsWithTheSignatureAndStatesItsSize() {
    var bytes = PrismPaintWriter.ToBytes(_Picture(320, 200, 4));

    Assert.Multiple(() => {
      Assert.That(System.Text.Encoding.ASCII.GetString(bytes, 0, 3), Is.EqualTo("PNT"));
      Assert.That((bytes[8] << 8) | bytes[9], Is.EqualTo(320));
      Assert.That((bytes[10] << 8) | bytes[11], Is.EqualTo(200));
      Assert.That((bytes[12] << 8) | bytes[13], Is.EqualTo(4));
    });
  }
}
