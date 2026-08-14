using System;
using System.Diagnostics;
using Hawkynt.FileFormats.Images.Tests;
using System.IO;
using FileFormat.Bmp;
using FileFormat.Core;

namespace FileFormat.Bmp.Tests;

/// <summary>
/// A four-bit run-length bitmap, which nothing here could read back until recently.
/// </summary>
/// <remarks>
/// The reader had a branch for the eight-bit form and none for this one, so a four-bit file fell
/// through to the uncompressed path and its opcodes were laid out as pixels — confident noise. The
/// writer was wrong in the same area and in the opposite direction: it handed the run-length coder a
/// row packed two pixels to the byte where the coder reads one index a byte, so every pair became a
/// single index and the coder walked off the end of the row.
/// <para/>
/// Neither fault could show while the other stood, which is why this asserts against ImageMagick
/// rather than against our own reader.
/// </remarks>
[TestFixture]
public sealed class Rle4WriterTests {

  private static RawImage _Picture(int width, int height) {
    // Runs long enough to code, and a stretch that must go out as literals.
    var palette = new byte[16 * 3];
    for (var i = 0; i < 16; ++i) {
      palette[i * 3] = (byte)(i * 17);
      palette[i * 3 + 1] = (byte)(255 - i * 17);
      palette[i * 3 + 2] = (byte)(i * 5);
    }

    var pixels = new byte[width * height];
    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x)
      pixels[y * width + x] = (byte)(x < width / 2 ? y % 16 : (x * 7 + y) % 16);

    return new() {
      Width = width, Height = height, Format = PixelFormat.Indexed8,
      PixelData = pixels, Palette = palette, PaletteCount = 16,
    };
  }

  [Test]
  [Category("Integration")]
  public void WhatIsWrittenComesBackThroughOurOwnReader() {
    var source = _Picture(37, 11);
    var bytes = BmpWriter.ToBytes(BmpFile.FromRawImage(source) with { Compression = BmpCompression.Rle4 });

    var decoded = BmpFile.ToRawImage(BmpReader.FromBytes(bytes));
    var expected = PixelConverter.Convert(source, PixelFormat.Rgb24).PixelData;

    Assert.Multiple(() => {
      Assert.That(decoded.Width, Is.EqualTo(37));
      Assert.That(decoded.Height, Is.EqualTo(11));
      Assert.That(PixelConverter.Convert(decoded, PixelFormat.Rgb24).PixelData, Is.EqualTo(expected));
    });
  }

  [Test]
  [Category("Conformance")]
  public void SomethingElseReadsItToo() {
    // A width that is not a multiple of eight, and an odd one at that, so a row whose last byte
    // holds one pixel rather than two is covered.
    var bytes = BmpWriter.ToBytes(BmpFile.FromRawImage(_Picture(37, 11)) with { Compression = BmpCompression.Rle4 });

    var directory = Directory.CreateTempSubdirectory("rle4");
    try {
      var path = Path.Combine(directory.FullName, "sample.bmp");
      File.WriteAllBytes(path, bytes);

      using var identify = ExternalTool.StartOrIgnore("identify", $"-format \"%wx%h\" \"{path}\"");

      var reported = identify.StandardOutput.ReadToEnd().Trim().Trim('"');
      identify.WaitForExit();

      if (identify.ExitCode != 0)
        Assert.Fail($"ImageMagick refused a four-bit run-length bitmap we wrote: {identify.StandardError.ReadToEnd().Trim()}");

      Assert.That(reported, Is.EqualTo("37x11"));
    } finally {
      try { directory.Delete(recursive: true); } catch { /* best effort */ }
    }
  }
}
