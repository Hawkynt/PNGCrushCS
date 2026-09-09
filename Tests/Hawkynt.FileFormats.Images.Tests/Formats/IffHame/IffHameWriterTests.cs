using System;
using System.IO;
using FileFormat.Core;
using FileFormat.IffHame;
using FileFormat.Ilbm;

namespace FileFormat.IffHame.Tests;

[TestFixture]
public sealed class IffHameWriterTests {

  [Test]
  [Category("Unit")]
  public void WrittenFile_IsFourPlaneHiresIlbmWithCookieAndPalette() {
    var file = IffHameFile.FromRawImage(_Picture(320, 8));

    var bytes = IffHameWriter.ToBytes(file);
    var carrier = IlbmReader.FromBytes(bytes);

    Assert.Multiple(() => {
      Assert.That(carrier.Width, Is.EqualTo(640));
      Assert.That(carrier.Height, Is.EqualTo(9));
      Assert.That(carrier.NumPlanes, Is.EqualTo(4));
      Assert.That(carrier.Compression, Is.EqualTo(IlbmCompression.ByteRun1));
      Assert.That(carrier.ViewportMode & 0x8000, Is.Not.Zero);
      Assert.That(carrier.ViewportMode & 0x0800, Is.Zero, "HAM-E uses a HIRES carrier, not Amiga HAM mode");
      Assert.That(carrier.Palette, Has.Length.EqualTo(16 * 3));
    });

    var cookie = IffHameCodec.MagicCookie;
    for (var i = 0; i < cookie.Length; ++i)
      Assert.That(_LogicalByte(carrier, i), Is.EqualTo(cookie[i]), $"cookie byte {i}");

    Assert.That(_LogicalByte(carrier, cookie.Length), Is.EqualTo(IffHameCodec.HoldAndModifyMode));
    for (var i = 0; i < IffHameCodec.PaletteEntriesPerLine * 3; ++i)
      Assert.That(_LogicalByte(carrier, cookie.Length + 1 + i), Is.EqualTo(file.Palette[i]), $"palette byte {i}");
  }

  [Test]
  [Category("Unit")]
  public void ToStream_MatchesToBytes() {
    var file = IffHameFile.FromRawImage(_Picture(320, 4));
    var expected = IffHameWriter.ToBytes(file);

    using var stream = new MemoryStream();
    IffHameWriter.ToStream(file, stream);

    Assert.That(stream.ToArray(), Is.EqualTo(expected));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_NormalWidth_IsAccepted() {
    Assert.DoesNotThrow(() => IffHameFile.FromRawImage(_Picture(320, 1)));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_BelowNormalWidth_ThrowsArgumentException() {
    Assert.Throws<ArgumentException>(() => IffHameFile.FromRawImage(_Picture(319, 1)));
  }

  [Test]
  [Category("Unit")]
  public void TooNarrow_ThrowsArgumentException() {
    var file = new IffHameFile {
      Width = IffHameFile.MinimumWidth - 1,
      Height = 1,
      Palette = new byte[IffHameCodec.PaletteEntriesPerLine * 3],
      PaletteCount = IffHameCodec.PaletteEntriesPerLine,
      PixelData = new byte[IffHameFile.MinimumWidth - 1],
    };

    Assert.Throws<ArgumentException>(() => IffHameWriter.ToBytes(file));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_TooWide_ThrowsArgumentException() {
    var image = _Picture(IffHameFile.MaximumWidth + 1, 1);
    Assert.Throws<ArgumentException>(() => IffHameFile.FromRawImage(image));
  }

  private static byte _LogicalByte(IlbmFile carrier, int index) {
    var palette = carrier.Palette!;
    var high = IffHameCodec.CarrierIndexToNibble(carrier.PixelData[index * 2], palette);
    var low = IffHameCodec.CarrierIndexToNibble(carrier.PixelData[index * 2 + 1], palette);
    return (byte)((high << 4) | low);
  }

  private static RawImage _Picture(int width, int height) {
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
