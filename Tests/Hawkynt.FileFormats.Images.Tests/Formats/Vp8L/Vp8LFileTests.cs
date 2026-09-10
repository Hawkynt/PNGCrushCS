using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Vp8L.Tests;

[TestFixture]
public sealed class Vp8LFileTests {

  [Test]
  [Category("Unit")]
  public void Encode_Read_Write_RoundTripsRgbaExactly() {
    const int width = 4;
    const int height = 3;
    var pixels = new byte[width * height * 4];
    for (var i = 0; i < width * height; ++i) {
      var offset = i * 4;
      pixels[offset] = (byte)(i * 19);
      pixels[offset + 1] = (byte)(255 - i * 11);
      pixels[offset + 2] = (byte)(i * 7);
      pixels[offset + 3] = (byte)(i * 23);
    }

    var source = new RawImage {
      Width = width,
      Height = height,
      Format = PixelFormat.Rgba32,
      PixelData = pixels,
    };

    var encoded = FormatIO.Encode<Vp8LFile>(source);
    var parsed = FormatIO.Read<Vp8LFile>(encoded);
    var rewritten = FormatIO.Write(parsed);
    var decoded = Vp8LFile.ToRawImage(parsed);

    Assert.Multiple(() => {
      Assert.That(encoded[0], Is.EqualTo(0x2F));
      Assert.That(FormatIO.MatchesSignature<Vp8LFile>(encoded), Is.True);
      Assert.That((parsed.Width, parsed.Height), Is.EqualTo((width, height)));
      Assert.That(parsed.AlphaHint, Is.True);
      Assert.That(rewritten, Is.EqualTo(encoded));
      Assert.That(decoded.Format, Is.EqualTo(PixelFormat.Rgba32));
      Assert.That(decoded.PixelData, Is.EqualTo(source.PixelData));
    });
  }

  [Test]
  [Category("Unit")]
  public void AlphaHintClear_DoesNotDiscardAlphaThatTheBitstreamActuallyContains() {
    var source = new RawImage {
      Width = 2,
      Height = 2,
      Format = PixelFormat.Rgba32,
      PixelData = [
        10, 20, 30, 0,
        40, 50, 60, 64,
        70, 80, 90, 128,
        100, 110, 120, 255,
      ],
    };

    var encoded = FormatIO.Encode<Vp8LFile>(source);
    encoded[4] &= 0xEF; // Bit 28 of the little-endian header: alpha_is_used = 0.

    var parsed = FormatIO.Read<Vp8LFile>(encoded);
    var decoded = Vp8LFile.ToRawImage(parsed);

    Assert.Multiple(() => {
      Assert.That(parsed.AlphaHint, Is.False);
      Assert.That(decoded.Format, Is.EqualTo(PixelFormat.Rgba32));
      Assert.That(decoded.PixelData, Is.EqualTo(source.PixelData));
    });
  }

  [Test]
  [Category("Unit")]
  public void OpaqueImage_DecodesAsRgb24() {
    var source = new RawImage {
      Width = 3,
      Height = 2,
      Format = PixelFormat.Rgb24,
      PixelData = [
        1, 2, 3, 4, 5, 6, 7, 8, 9,
        10, 11, 12, 13, 14, 15, 16, 17, 18,
      ],
    };

    var decoded = FormatIO.Decode<Vp8LFile>(FormatIO.Encode<Vp8LFile>(source));

    Assert.Multiple(() => {
      Assert.That(decoded.Format, Is.EqualTo(PixelFormat.Rgb24));
      Assert.That(decoded.PixelData, Is.EqualTo(source.PixelData));
    });
  }

  [Test]
  [Category("Unit")]
  public void Reader_RejectsNonZeroVersion() {
    var source = new RawImage {
      Width = 1,
      Height = 1,
      Format = PixelFormat.Rgb24,
      PixelData = [1, 2, 3],
    };
    var encoded = FormatIO.Encode<Vp8LFile>(source);
    encoded[4] |= 0x20; // Version 1 in bits 29..31.

    Assert.Multiple(() => {
      Assert.That(FormatIO.MatchesSignature<Vp8LFile>(encoded), Is.Null);
      Assert.Throws<InvalidDataException>(() => FormatIO.Read<Vp8LFile>(encoded));
    });
  }
}
