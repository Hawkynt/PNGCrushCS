using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Vp8.Tests;

[TestFixture]
public sealed class Vp8FileTests {

  [Test]
  [Category("Unit")]
  public void Signature_RecognisesKeyFrameAndRejectsInterFrame() {
    byte[] keyFrame = [0x10, 0x00, 0x00, 0x9D, 0x01, 0x2A, 0x10, 0x00, 0x0C, 0x00];
    var interFrame = keyFrame[..];
    interFrame[0] |= 0x01;

    Assert.Multiple(() => {
      Assert.That(FormatIO.MatchesSignature<Vp8File>(keyFrame), Is.True);
      Assert.That(FormatIO.MatchesSignature<Vp8File>(interFrame), Is.Null);
    });
  }

  [Test]
  [Category("Unit")]
  public void Reader_RejectsReservedVersionEvenThoughTheVp8SignatureStillMatches() {
    byte[] data = [0x18, 0x00, 0x00, 0x9D, 0x01, 0x2A, 0x01, 0x00, 0x01, 0x00];

    Assert.Multiple(() => {
      Assert.That(FormatIO.MatchesSignature<Vp8File>(data), Is.True);
      Assert.Throws<NotSupportedException>(() => FormatIO.Read<Vp8File>(data));
    });
  }

  [Test]
  [Category("Unit")]
  public void Reader_RejectsTruncatedFirstPartition() {
    // Key frame, show_frame=1, first partition length=5, but no bytes follow the ten-byte header.
    byte[] data = [0xB0, 0x00, 0x00, 0x9D, 0x01, 0x2A, 0x01, 0x00, 0x01, 0x00];
    Assert.Throws<InvalidDataException>(() => FormatIO.Read<Vp8File>(data));
  }

  [Test]
  [Category("Unit")]
  public void Encode_Read_Write_DecodesAsRgbAtTheOriginalDimensions() {
    const int width = 16;
    const int height = 16;
    var pixels = new byte[width * height * 3];
    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x) {
      var offset = (y * width + x) * 3;
      pixels[offset] = (byte)(x * 17);
      pixels[offset + 1] = (byte)(y * 17);
      pixels[offset + 2] = (byte)((x ^ y) * 17);
    }

    var source = new RawImage {
      Width = width,
      Height = height,
      Format = PixelFormat.Rgb24,
      PixelData = pixels,
    };

    var encoded = FormatIO.Encode<Vp8File>(source);
    var parsed = FormatIO.Read<Vp8File>(encoded);
    var rewritten = FormatIO.Write(parsed);
    var decoded = Vp8File.ToRawImage(parsed);

    Assert.Multiple(() => {
      Assert.That(encoded.Length, Is.GreaterThan(10));
      Assert.That(encoded.AsSpan(3, 3).ToArray(), Is.EqualTo(new byte[] { 0x9D, 0x01, 0x2A }));
      Assert.That((parsed.Width, parsed.Height), Is.EqualTo((width, height)));
      Assert.That(rewritten, Is.EqualTo(encoded));
      Assert.That((decoded.Width, decoded.Height), Is.EqualTo((width, height)));
      Assert.That(decoded.Format, Is.EqualTo(PixelFormat.Rgb24));
      Assert.That(decoded.PixelData.Length, Is.EqualTo(width * height * 3));
    });
  }
}
