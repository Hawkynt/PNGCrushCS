using System;
using System.IO;
using FileFormat.Core;
using FileFormat.IffHame;

namespace FileFormat.IffHame.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_RepresentablePixelsArePreserved() {
    var original = _FourColourPicture(320, 8);

    var encoded = IffHameFile.FromRawImage(original);
    var bytes = IffHameWriter.ToBytes(encoded);
    var restored = IffHameFile.ToRawImage(IffHameReader.FromBytes(bytes));

    Assert.Multiple(() => {
      Assert.That(restored.Width, Is.EqualTo(original.Width));
      Assert.That(restored.Height, Is.EqualTo(original.Height));
      Assert.That(restored.Format, Is.EqualTo(PixelFormat.Rgb24));
      Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
    });
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllPaletteBanksAndCommandsArePreserved() {
    var palette = new byte[IffHameCodec.MaximumPaletteEntries * 3];
    for (var i = 0; i < palette.Length; ++i)
      palette[i] = (byte)(i * 37 + 11);

    var commands = new byte[320 * 3];
    for (var i = 0; i < commands.Length; ++i)
      commands[i] = (byte)(i * 29 + 7);

    var original = new IffHameFile {
      Width = 320,
      Height = 3,
      Interlaced = true,
      Palette = palette,
      PaletteCount = IffHameCodec.MaximumPaletteEntries,
      PixelData = commands,
    };

    var restored = IffHameReader.FromBytes(IffHameWriter.ToBytes(original));

    Assert.Multiple(() => {
      Assert.That(restored.Width, Is.EqualTo(original.Width));
      Assert.That(restored.Height, Is.EqualTo(original.Height));
      Assert.That(restored.Interlaced, Is.True);
      Assert.That(restored.PaletteCount, Is.EqualTo(original.PaletteCount));
      Assert.That(restored.Palette, Is.EqualTo(original.Palette));
      Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
    });
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaFile() {
    var original = IffHameFile.FromRawImage(_FourColourPicture(320, 4));
    var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".hame");
    try {
      IffHameWriter.ToFile(original, new FileInfo(tempPath));
      var restored = IffHameReader.FromFile(new FileInfo(tempPath));

      Assert.Multiple(() => {
        Assert.That(restored.Width, Is.EqualTo(original.Width));
        Assert.That(restored.Height, Is.EqualTo(original.Height));
        Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
      });
    } finally {
      if (File.Exists(tempPath))
        File.Delete(tempPath);
    }
  }

  private static RawImage _FourColourPicture(int width, int height) {
    ReadOnlySpan<byte> colours = [
      0x00, 0x00, 0x00,
      0xFF, 0x00, 0x00,
      0x00, 0xFF, 0x00,
      0x00, 0x00, 0xFF,
    ];

    var pixels = new byte[width * height * 3];
    for (var i = 0; i < width * height; ++i)
      colours.Slice(i % 4 * 3, 3).CopyTo(pixels.AsSpan(i * 3));

    return new() {
      Width = width,
      Height = height,
      Format = PixelFormat.Rgb24,
      PixelData = pixels,
    };
  }
}
