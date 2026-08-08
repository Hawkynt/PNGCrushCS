using System;
using System.Diagnostics;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Bmp.Tests;

/// <summary>
/// A four- or one-bit bitmap whose row is not a whole number of bytes.
/// </summary>
/// <remarks>
/// The writer had no counterpart to the reader's unpadding. A picture of this depth is held here as
/// one unbroken stream of indices with nothing between its rows and a BMP starts every row on a fresh
/// byte, and the writer handed the stream over as though it were already aligned. That costs nothing
/// while the row happens to be a whole number of bytes — a four-bit picture of even width, a one-bit
/// one whose width is a multiple of eight — and slides every row after the first by part of a byte
/// otherwise: a picture that leans rather than one that is obviously broken.
/// <para/>
/// It could not show against our own reader, which was right, so this asserts against ImageMagick as
/// well. It turned up when a clip-art catalogue of four-bit thumbnails sixty-nine pixels wide was
/// written back and came out different.
/// </remarks>
[TestFixture]
public sealed class SubByteRowPaddingTests {

  private static RawImage _Picture(int width, int height, int bits) {
    var count = 1 << bits;
    var palette = new byte[count * 3];
    for (var i = 0; i < count; ++i) {
      palette[i * 3] = (byte)(i * 255 / (count - 1));
      palette[i * 3 + 1] = (byte)(255 - i * 255 / (count - 1));
      palette[i * 3 + 2] = (byte)(i * 37);
    }

    var indices = new byte[(width * height * bits + 7) / 8];
    for (var i = 0; i < indices.Length; ++i)
      indices[i] = (byte)(i * 37 & 0xFF);

    return new() {
      Width = width, Height = height,
      Format = bits == 4 ? PixelFormat.Indexed4 : PixelFormat.Indexed1,
      PixelData = indices, Palette = palette, PaletteCount = count,
    };
  }

  [Test]
  [Category("Integration")]
  [TestCase(69, 72, 4)]
  [TestCase(71, 8, 4)]
  [TestCase(64, 72, 4)]
  [TestCase(37, 11, 1)]
  [TestCase(69, 5, 1)]
  [TestCase(32, 8, 1)]
  public void RoundTrip_ARowThatIsNotWholeBytes_KeepsItsRowsAligned(int width, int height, int bits) {
    var source = _Picture(width, height, bits);
    var decoded = BmpFile.ToRawImage(BmpReader.FromBytes(BmpWriter.ToBytes(BmpFile.FromRawImage(source))));

    Assert.That(
      PixelConverter.Convert(decoded, PixelFormat.Rgb24).PixelData,
      Is.EqualTo(PixelConverter.Convert(source, PixelFormat.Rgb24).PixelData));
  }

  [Test]
  [Category("Conformance")]
  public void SomethingElseSeesTheSamePixels() {
    var source = _Picture(69, 72, 4);
    var expected = PixelConverter.Convert(source, PixelFormat.Rgb24).PixelData;

    var directory = Directory.CreateTempSubdirectory("subbyte");
    try {
      var bitmap = Path.Combine(directory.FullName, "picture.bmp");
      var rendered = Path.Combine(directory.FullName, "rendered.ppm");
      File.WriteAllBytes(bitmap, BmpWriter.ToBytes(BmpFile.FromRawImage(source)));

      using var convert = Process.Start(new ProcessStartInfo("magick", $"\"{bitmap}\" -depth 8 \"{rendered}\"") {
        RedirectStandardOutput = true, RedirectStandardError = true,
      });

      if (convert == null)
        Assert.Ignore("no ImageMagick here to ask");

      var complaint = convert!.StandardError.ReadToEnd();
      convert.WaitForExit();
      if (convert.ExitCode != 0 || !File.Exists(rendered))
        Assert.Ignore($"ImageMagick would not read it here: {complaint.Trim()}");

      var ppm = File.ReadAllBytes(rendered);
      Assert.That(ppm[^expected.Length..], Is.EqualTo(expected));
    } finally {
      try { directory.Delete(recursive: true); } catch { /* best effort */ }
    }
  }
}
