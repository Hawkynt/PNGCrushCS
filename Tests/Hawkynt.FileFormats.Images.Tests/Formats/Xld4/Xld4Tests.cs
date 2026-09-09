using System;
using System.IO;
using System.Text;
using FileFormat.Core;
using FileFormat.Xld4;

namespace FileFormat.Xld4.Tests;

[TestFixture]
public sealed class Xld4Tests {

  private static readonly byte[] _Palette = _BuildPalette();

  private static byte[] _BuildPalette() {
    var result = new byte[Xld4File.ColorCount * 3];
    for (var i = 0; i < Xld4File.ColorCount; ++i) {
      result[i * 3] = (byte)(i * 17);
      result[i * 3 + 1] = (byte)((15 - i) * 17);
      result[i * 3 + 2] = (byte)(((i * 5) & 15) * 17);
    }
    return result;
  }

  private static RawImage _Image(Func<int, int, byte> pixel) {
    var pixels = new byte[Xld4File.Width * Xld4File.Height];
    for (var y = 0; y < Xld4File.Height; ++y)
    for (var x = 0; x < Xld4File.Width; ++x)
      pixels[y * Xld4File.Width + x] = pixel(x, y);

    return new() {
      Width = Xld4File.Width,
      Height = Xld4File.Height,
      Format = PixelFormat.Indexed8,
      PixelData = pixels,
      Palette = _Palette[..],
      PaletteCount = Xld4File.ColorCount,
    };
  }

  [Test]
  [Category("Unit")]
  public void Writer_RoundTrip_PreservesDictionaryAndRunLengthStreams() {
    var source = _Image(static (x, y) => y < Xld4File.Height / 2
      ? (byte)((x + y) & 15)
      : (byte)(((x / 80) + (y / 25)) & 15));

    var encoded = FormatIO.Encode<Xld4File>(source);
    var decoded = FormatIO.Decode<Xld4File>(encoded);

    Assert.Multiple(() => {
      Assert.That(encoded.Length, Is.LessThanOrEqualTo(ushort.MaxValue));
      Assert.That(encoded[2], Is.EqualTo(2));
      Assert.That(encoded[8] | (encoded[9] << 8), Is.EqualTo(encoded.Length));
      Assert.That(Encoding.ASCII.GetString(encoded, 11, 5), Is.EqualTo("MAJYO"));
      Assert.That(decoded.Width, Is.EqualTo(Xld4File.Width));
      Assert.That(decoded.Height, Is.EqualTo(Xld4File.Height));
      Assert.That(decoded.PixelData, Is.EqualTo(source.PixelData));
      Assert.That(decoded.Palette, Is.EqualTo(_Palette));
    });
  }

  [Test]
  [Category("Unit")]
  public void Writer_RoundTrip_UsesAllPaletteRegistersInLogicalOrder() {
    var source = _Image(static (x, y) => (byte)(((x / 40) + y) & 15));

    var decoded = FormatIO.Decode<Xld4File>(FormatIO.Encode<Xld4File>(source));

    Assert.That(decoded.PixelData, Is.EqualTo(source.PixelData));
    Assert.That(decoded.Palette, Is.EqualTo(_Palette));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_WrongDimensions_AreRejected() {
    var source = new RawImage {
      Width = Xld4File.Width,
      Height = Xld4File.Height - 1,
      Format = PixelFormat.Indexed8,
      PixelData = new byte[Xld4File.Width * (Xld4File.Height - 1)],
      Palette = _Palette[..],
      PaletteCount = Xld4File.ColorCount,
    };

    Assert.Throws<ArgumentException>(() => Xld4File.FromRawImage(source));
  }

  [Test]
  [Category("Unit")]
  public void Writer_PaletteIndexOutsideFourBits_IsRejected() {
    var pixels = new byte[Xld4File.Width * Xld4File.Height];
    pixels[^1] = 16;
    var file = new Xld4File { Pixels = pixels, Palette = _Palette[..] };

    Assert.Throws<ArgumentException>(() => Xld4Writer.ToBytes(file));
  }

  [Test]
  [Category("Unit")]
  public void Writer_HighEntropyPictureThatCannotFitLengthWord_IsRejected() {
    var random = new Random(0x584C4434);
    var pixels = new byte[Xld4File.Width * Xld4File.Height];
    random.NextBytes(pixels);
    for (var i = 0; i < pixels.Length; ++i)
      pixels[i] &= 15;

    var file = new Xld4File { Pixels = pixels, Palette = _Palette[..] };
    var exception = Assert.Throws<InvalidDataException>(() => Xld4Writer.ToBytes(file));
    Assert.That(exception!.Message, Does.Contain("sixteen bits"));
  }
}
