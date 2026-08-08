using System;
using FileFormat.Core;

namespace FileFormat.PalmImageViewer.Tests;

[TestFixture]
public sealed class FromRawImageTests {

  /// <summary>A width that is neither a multiple of eight nor of sixteen.</summary>
  private const int _WIDTH = 37;

  private const int _HEIGHT = 11;

  /// <summary>Greys drawn from the sixteen-step ramp, so nothing has to be rounded to reach it.</summary>
  private static RawImage _Ramp(int width, int height) {
    var data = new byte[width * height * 3];
    for (var i = 0; i < width * height; ++i) {
      var value = (byte)(i % 16 * 17);
      data[i * 3] = data[i * 3 + 1] = data[i * 3 + 2] = value;
    }

    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = data };
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_SixteenGreys_ReproducesExactly() {
    var source = _Ramp(_WIDTH, _HEIGHT);
    var file = PalmImageViewerFile.FromRawImage(source);
    var decoded = PalmImageViewerFile.ToRawImage(
      PalmImageViewerReader.FromBytes(PalmImageViewerWriter.ToBytes(file)));

    Assert.Multiple(() => {
      Assert.That((decoded.Width, decoded.Height), Is.EqualTo((_WIDTH, _HEIGHT)));
      Assert.That(file.BitsPerPixel, Is.EqualTo(4));
      Assert.That(PixelConverter.Convert(decoded, PixelFormat.Rgb24).PixelData, Is.EqualTo(source.PixelData));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_KeepsWhateverSizeItIsGiven() {
    // The record states the size, so nothing is scaled to reach it.
    var wide = PalmImageViewerFile.FromRawImage(_Ramp(200, 3));
    var tall = PalmImageViewerFile.FromRawImage(_Ramp(3, 200));

    Assert.Multiple(() => {
      Assert.That((wide.Width, wide.Height), Is.EqualTo((200, 3)));
      Assert.That((tall.Width, tall.Height), Is.EqualTo((3, 200)));

      // Three pixels at four bits a row would read back as five, so a shallower depth is chosen.
      Assert.That(tall.BitsPerPixel, Is.EqualTo(2));
    });
  }

  [Test]
  [Category("Unit")]
  public void Compress_IsTheExactInverseOfTheReadersDecompression() {
    // Every case the coding has: a run ending exactly on the 128-byte boundary, a longer one that
    // has to be split, single bytes between runs, and a literal run of exactly 128.
    var source = new byte[128 + 300 + 1 + 128 + 3];
    var at = 0;
    for (var i = 0; i < 128; ++i)
      source[at++] = 0xAA;

    for (var i = 0; i < 300; ++i)
      source[at++] = 0x55;

    source[at++] = 0x01;
    for (var i = 0; i < 128; ++i)
      source[at++] = (byte)(i * 7 + 1);

    source[at++] = 0x02;
    source[at++] = 0x03;
    source[at] = 0x04;

    var compressed = PalmImageViewerWriter.Compress(source);

    // Two pixels a byte, so the row is as long as the bytes under test and the depth reads back.
    var file = PalmImageViewerReader.FromBytes(PalmImageViewerWriter.ToBytes(new() {
      Width = source.Length * 2, Height = 1, BitsPerPixel = 4, Name = string.Empty, PixelData = source,
    }));

    Assert.Multiple(() => {
      Assert.That(compressed, Has.Length.LessThan(source.Length));
      Assert.That(file.PixelData, Is.EqualTo(source));
    });
  }
}
