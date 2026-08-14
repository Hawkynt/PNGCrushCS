using System.IO;
using System.Text;
using FileFormat.Miff;
using Hawkynt.FileFormats.Images.Tests;

namespace FileFormat.Miff.Tests;

/// <summary>
/// A written MIFF states its alpha channel in <c>alpha-trait</c>, which is the field ImageMagick
/// takes the channel layout from.
/// </summary>
/// <remarks>
/// <c>type=TrueColorAlpha</c> does not tell it: ImageMagick's own alpha files carry no <c>type</c>
/// line at all, only <c>alpha-trait=Blend</c> and the older <c>matte=True</c>. Writing four samples
/// a pixel and saying so only in <c>type</c> therefore hands it a file it reads three at a time, so
/// every fourth byte becomes the next pixel's red and the picture shears — 748 of 2257 pixels
/// different on a 61x37 sample, where our own reader, which believes <c>type</c>, read the same file
/// perfectly. A disagreement only something outside this project could show.
/// <para/>
/// Proved by hand before it was fixed: inserting either field into the written header, changing no
/// sample byte, took ImageMagick's reading of that same file from 748 differing pixels to none.
/// </remarks>
[TestFixture]
public sealed class MiffAlphaTraitTests {

  private static byte[] _Rgba(int width, int height) {
    var pixels = new byte[width * height * 4];
    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x) {
      var at = (y * width + x) * 4;
      pixels[at] = (byte)(x * 7);
      pixels[at + 1] = (byte)(y * 20);
      pixels[at + 2] = (byte)(255 - x * 5);
      pixels[at + 3] = (byte)(0x40 + x % 3 * 0x50);
    }

    return pixels;
  }

  private static byte[] _Rgb(int width, int height) {
    var pixels = new byte[width * height * 3];
    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x) {
      var at = (y * width + x) * 3;
      pixels[at] = (byte)(x * 7);
      pixels[at + 1] = (byte)(y * 20);
      pixels[at + 2] = (byte)(255 - x * 5);
    }

    return pixels;
  }

  /// <summary>Hands ImageMagick the file and takes back the samples it read out of it.</summary>
  private static byte[] _WhatImageMagickReads(byte[] miff, string rawFormat) {
    var directory = Directory.CreateTempSubdirectory("miffalpha");
    try {
      var path = Path.Combine(directory.FullName, "sample.miff");
      var readBack = Path.Combine(directory.FullName, "sample.raw");
      File.WriteAllBytes(path, miff);

      using var magick = ExternalTool.StartOrIgnore("magick", $"\"{path}\" -depth 8 {rawFormat}:\"{readBack}\"");
      var complaint = magick.StandardError.ReadToEnd().Trim();
      magick.WaitForExit();

      if (magick.ExitCode != 0)
        Assert.Fail($"ImageMagick refused the MIFF we wrote: {complaint}");

      return File.ReadAllBytes(readBack);
    } finally {
      try { directory.Delete(recursive: true); } catch { /* best effort */ }
    }
  }

  [Test]
  [Category("Conformance")]
  public void SomethingElseReadsOurAlphaChannel() {
    var pixels = _Rgba(37, 11);
    var bytes = MiffWriter.ToBytes(new MiffFile {
      Width = 37, Height = 11, Depth = 8,
      ColorClass = MiffColorClass.DirectClass, Compression = MiffCompression.None,
      Colorspace = "sRGB", Type = "TrueColorAlpha", PixelData = pixels,
    });

    Assert.That(_WhatImageMagickReads(bytes, "RGBA"), Is.EqualTo(pixels));
  }

  /// <summary>Run-length packets are four samples wide too, and their width is stated the same way.</summary>
  [Test]
  [Category("Conformance")]
  public void SomethingElseReadsOurAlphaChannel_RunLength() {
    var pixels = _Rgba(37, 11);
    var bytes = MiffWriter.ToBytes(new MiffFile {
      Width = 37, Height = 11, Depth = 8,
      ColorClass = MiffColorClass.DirectClass, Compression = MiffCompression.Rle,
      Colorspace = "sRGB", Type = "TrueColorAlpha", PixelData = pixels,
    });

    Assert.That(_WhatImageMagickReads(bytes, "RGBA"), Is.EqualTo(pixels));
  }

  /// <summary>A picture with no alpha must not gain one by being written.</summary>
  [Test]
  [Category("Conformance")]
  public void SomethingElseStillReadsAPictureWithoutAlpha() {
    var pixels = _Rgb(37, 11);
    var bytes = MiffWriter.ToBytes(new MiffFile {
      Width = 37, Height = 11, Depth = 8,
      ColorClass = MiffColorClass.DirectClass, Compression = MiffCompression.None,
      Colorspace = "sRGB", Type = "TrueColor", PixelData = pixels,
    });

    Assert.That(_WhatImageMagickReads(bytes, "RGB"), Is.EqualTo(pixels));
  }

  [Test]
  [Category("Unit")]
  public void Format_AlphaPicture_StatesTheChannel() {
    var header = Encoding.ASCII.GetString(MiffHeaderParser.Format(new() {
      Width = 2, Height = 1, Depth = 8,
      ColorClass = MiffColorClass.DirectClass, Colorspace = "sRGB", Type = "TrueColorAlpha",
      PixelData = new byte[8],
    }));

    Assert.Multiple(() => {
      Assert.That(header, Does.Contain("alpha-trait=Blend"));
      Assert.That(header, Does.Contain("matte=True"));
    });
  }

  [Test]
  [Category("Unit")]
  public void Format_OpaquePicture_StatesNoChannel() {
    var header = Encoding.ASCII.GetString(MiffHeaderParser.Format(new() {
      Width = 2, Height = 1, Depth = 8,
      ColorClass = MiffColorClass.DirectClass, Colorspace = "sRGB", Type = "TrueColor",
      PixelData = new byte[6],
    }));

    Assert.Multiple(() => {
      Assert.That(header, Does.Contain("alpha-trait=Undefined"));
      Assert.That(header, Does.Not.Contain("matte=True"));
    });
  }
}
