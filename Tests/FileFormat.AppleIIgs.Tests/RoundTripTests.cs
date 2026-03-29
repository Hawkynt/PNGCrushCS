using System;
using FileFormat.AppleIIgs;

namespace FileFormat.AppleIIgs.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_Mode320_AllFieldsPreserved() {
    var original = _BuildValidFile(AppleIIgsMode.Mode320);

    var bytes = AppleIIgsWriter.ToBytes(original);
    var restored = AppleIIgsReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.Mode, Is.EqualTo(original.Mode));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
    Assert.That(restored.Scbs, Is.EqualTo(original.Scbs));
    Assert.That(restored.Palettes, Is.EqualTo(original.Palettes));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_Mode640_AllFieldsPreserved() {
    var original = _BuildValidFile(AppleIIgsMode.Mode640);

    var bytes = AppleIIgsWriter.ToBytes(original);
    var restored = AppleIIgsReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.Mode, Is.EqualTo(original.Mode));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
    Assert.That(restored.Scbs, Is.EqualTo(original.Scbs));
    Assert.That(restored.Palettes, Is.EqualTo(original.Palettes));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_PalettePreserved() {
    var palettes = new short[256];
    for (var i = 0; i < palettes.Length; ++i)
      palettes[i] = (short)(i * 13 % 4096);

    var original = new AppleIIgsFile {
      Width = 320,
      Height = 200,
      Mode = AppleIIgsMode.Mode320,
      PixelData = new byte[32000],
      Scbs = new byte[200],
      Palettes = palettes
    };

    var bytes = AppleIIgsWriter.ToBytes(original);
    var restored = AppleIIgsReader.FromBytes(bytes);

    Assert.That(restored.Palettes, Is.EqualTo(original.Palettes));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ScbPreserved() {
    var scbs = new byte[200];
    for (var i = 0; i < scbs.Length; ++i)
      scbs[i] = (byte)(i % 16); // palette numbers 0-15, no 640 mode bit

    var original = new AppleIIgsFile {
      Width = 320,
      Height = 200,
      Mode = AppleIIgsMode.Mode320,
      PixelData = new byte[32000],
      Scbs = scbs,
      Palettes = new short[256]
    };

    var bytes = AppleIIgsWriter.ToBytes(original);
    var restored = AppleIIgsReader.FromBytes(bytes);

    Assert.That(restored.Scbs, Is.EqualTo(original.Scbs));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllBytesMaxValue() {
    var pixelData = new byte[32000];
    Array.Fill(pixelData, (byte)0xFF);

    var scbs = new byte[200];
    Array.Fill(scbs, (byte)0xFF);

    var palettes = new short[256];
    Array.Fill(palettes, short.MaxValue);

    var original = new AppleIIgsFile {
      Width = 640,
      Height = 200,
      Mode = AppleIIgsMode.Mode640,
      PixelData = pixelData,
      Scbs = scbs,
      Palettes = palettes
    };

    var bytes = AppleIIgsWriter.ToBytes(original);
    var restored = AppleIIgsReader.FromBytes(bytes);

    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
    Assert.That(restored.Scbs, Is.EqualTo(original.Scbs));
    Assert.That(restored.Palettes, Is.EqualTo(original.Palettes));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_DimensionsAlwaysFixed() {
    var original = _BuildValidFile(AppleIIgsMode.Mode320);

    var bytes = AppleIIgsWriter.ToBytes(original);
    var restored = AppleIIgsReader.FromBytes(bytes);

    Assert.That(restored.Height, Is.EqualTo(200));
    Assert.That(restored.Width, Is.EqualTo(320));
  }

  private static AppleIIgsFile _BuildValidFile(AppleIIgsMode mode) {
    var pixelData = new byte[32000];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 7 % 256);

    var scbs = new byte[200];
    var scbBase = mode == AppleIIgsMode.Mode640 ? (byte)0x80 : (byte)0x00;
    for (var i = 0; i < scbs.Length; ++i)
      scbs[i] = (byte)(scbBase | (i % 16));

    var palettes = new short[256];
    for (var i = 0; i < palettes.Length; ++i)
      palettes[i] = (short)(i * 13 % 4096);

    return new AppleIIgsFile {
      Width = mode == AppleIIgsMode.Mode640 ? 640 : 320,
      Height = 200,
      Mode = mode,
      PixelData = pixelData,
      Scbs = scbs,
      Palettes = palettes
    };
  }
}
