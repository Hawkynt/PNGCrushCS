using System;
using System.IO;
using FileFormat.Core;
using FileFormat.JpegXr.JxrLib;
using NUnit.Framework;

namespace FileFormat.JpegXr.Tests;

/// <summary>
/// JPEG XR files jxrlib wrote, decoded and compared byte for byte with jxrlib's own decode of each
/// into the file's native pixel format.
/// </summary>
/// <remarks>
/// The files were written by jxrlib's encoder through a small harness on its glue API (so every
/// pixel format can be stated, the n-channel ones included) and decoded by the same library with
/// alpha requested as <c>JxrDecApp</c> requests it. Between them they cover the pixel-format
/// families — grey, bilevel, RGB at 8 and 16 bits, half, float, fixed point, 565, 101010, RGBE, CMYK
/// and CMYKA, n-channel with and without alpha — and the coding options: lossy and lossless,
/// planar and interleaved alpha, 4:2:0 with tiles, two-level overlap, spatial order, hard tile
/// boundaries in the original sub-version, unscaled arithmetic and a single pixel.
/// </remarks>
[TestFixture]
public sealed class JxrLibFixtureTests {

  private static byte[] _Fixture(string name)
    => File.ReadAllBytes(Path.Combine(TestContext.CurrentContext.TestDirectory, "JxrFixtures", name));

  [TestCase("8bppGray_ol2_lossy")]
  [TestCase("BlackWhite_white0")]
  [TestCase("16bppGrayFixedPoint_lossy")]
  [TestCase("24bppRGB_cf420_tiles")]
  [TestCase("24bppRGB_tiles_spatial")]
  [TestCase("24bppRGB_hard420_ol2_sub0")]
  [TestCase("24bppRGB_unscaled420")]
  [TestCase("8bppGray_size1x1_sub0")]
  [TestCase("32bppBGRA_lossy")]
  [TestCase("32bppBGRA_interleaved_lossy")]
  [TestCase("48bppRGBHalf_lossy")]
  [TestCase("128bppRGBAFloat_interleaved")]
  [TestCase("16bppRGB565_lossless")]
  [TestCase("32bppRGB101010_lossy")]
  [TestCase("32bppRGBE_lossy")]
  [TestCase("40bppCMYKAlpha_lossy")]
  [TestCase("64bppCMYK_lossless")]
  [TestCase("40bpp5Channels_lossy")]
  [TestCase("96bpp5ChannelsAlpha_lossy")]
  [Category("Unit")]
  public void DecodesByteForByteAsJxrlibDoes(string name) {
    var decoded = JxrLibDecoder.Decode(_Fixture(name + ".jxr"));
    var expected = _Fixture(name + ".raw");
    Assert.That(decoded.Pixels.Length, Is.EqualTo(expected.Length));
    var first = -1;
    for (var i = 0; i < expected.Length && first < 0; ++i)
      if (decoded.Pixels[i] != expected[i])
        first = i;
    Assert.That(first, Is.EqualTo(-1), $"{name}: first differing byte at {first}.");
  }

  [TestCase("8bppGray_ol2_lossy", PixelFormat.Gray8, 1)]
  [TestCase("BlackWhite_white0", PixelFormat.Gray8, 1)]
  [TestCase("16bppGrayFixedPoint_lossy", PixelFormat.GrayF32, 1)]
  [TestCase("24bppRGB_cf420_tiles", PixelFormat.Rgb24, 3)]
  [TestCase("32bppBGRA_lossy", PixelFormat.Rgba32, 4)]
  [TestCase("48bppRGBHalf_lossy", PixelFormat.RgbF32, 3)]
  [TestCase("128bppRGBAFloat_interleaved", PixelFormat.RgbaF32, 4)]
  [TestCase("16bppRGB565_lossless", PixelFormat.Rgb24, 3)]
  [TestCase("32bppRGB101010_lossy", PixelFormat.Rgb48, 3)]
  [TestCase("32bppRGBE_lossy", PixelFormat.RgbF32, 3)]
  [TestCase("40bppCMYKAlpha_lossy", PixelFormat.Rgba32, 5)]
  [TestCase("64bppCMYK_lossless", PixelFormat.Rgb48, 4)]
  [TestCase("40bpp5Channels_lossy", PixelFormat.Rgb24, 5)]
  [TestCase("96bpp5ChannelsAlpha_lossy", PixelFormat.Rgba64, 6)]
  [Category("Unit")]
  public void ReadsIntoTheNearestRaster(string name, PixelFormat format, int components) {
    var file = JpegXrReader.FromBytes(_Fixture(name + ".jxr"));
    var raster = JpegXrFile.ToRawImage(file);
    Assert.Multiple(() => {
      Assert.That(raster.Format, Is.EqualTo(format));
      Assert.That(file.ComponentCount, Is.EqualTo(components));
      Assert.That(file.SourcePixelFormat, Is.EqualTo(name[..name.IndexOf('_')]));
      Assert.That(file.SourceSamples, Is.EqualTo(_Fixture(name + ".raw")));
    });
  }

  /// <summary>Planar and interleaved alpha both reach the raster, from the bytes jxrlib decodes.</summary>
  [TestCase("32bppBGRA_lossy")]
  [TestCase("32bppBGRA_interleaved_lossy")]
  [Category("Unit")]
  public void AlphaReachesTheRaster(string name) {
    var native = _Fixture(name + ".raw");
    var raster = JpegXrFile.ToRawImage(JpegXrReader.FromBytes(_Fixture(name + ".jxr")));
    var pixels = raster.Width * raster.Height;
    var opaque = 0;
    for (var i = 0; i < pixels; ++i) {
      Assert.That(raster.PixelData[i * 4 + 3], Is.EqualTo(native[i * 4 + 3]));
      Assert.That(raster.PixelData[i * 4], Is.EqualTo(native[i * 4 + 2]), "BGRA comes back as RGBA");
      if (native[i * 4 + 3] == 255)
        ++opaque;
    }

    Assert.That(opaque, Is.LessThan(pixels), "the alpha plane is not trivially opaque");
  }

  [Test]
  [Category("Unit")]
  public void CmykIsTheLightTheInksLeave() {
    var native = _Fixture("64bppCMYK_lossless.raw");
    var raster = JpegXrFile.ToRawImage(JpegXrReader.FromBytes(_Fixture("64bppCMYK_lossless.jxr")));
    int Ink(int sample) => native[sample * 2] | (native[sample * 2 + 1] << 8);
    int Light(int sample) => (raster.PixelData[sample * 2] << 8) | raster.PixelData[sample * 2 + 1];
    for (var i = 0; i < 16; ++i) {
      var k = 65535 - Ink(i * 4 + 3);
      for (var c = 0; c < 3; ++c)
        Assert.That(Light(i * 3 + c), Is.EqualTo((int)(((long)(65535 - Ink(i * 4 + c)) * k + 32767) / 65535)));
    }
  }

  [Test]
  [Category("Unit")]
  public void AReadPictureWritesBackAsEightBit() {
    var file = JpegXrReader.FromBytes(_Fixture("48bppRGBHalf_lossy.jxr"));
    var again = JpegXrReader.FromBytes(JpegXrWriter.ToBytes(file));
    Assert.Multiple(() => {
      Assert.That((again.Width, again.Height), Is.EqualTo((file.Width, file.Height)));
      Assert.That(JpegXrFile.ToRawImage(again).Format, Is.EqualTo(PixelFormat.Rgb24));
    });
  }
}
