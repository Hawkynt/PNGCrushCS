using System;
using System.Threading.Tasks;
using FileFormat.Core;
using FileFormat.Sgi;
using Optimizer.Sgi;

namespace Optimizer.Sgi.Tests;

/// <summary>
/// The optimizer must never change the picture, and should usually make the file smaller.
/// </summary>
/// <remarks>
/// Both halves matter and the first matters more: a smaller file that decodes to different pixels is
/// not an optimization, it is corruption. Every test here decodes the result and compares it against
/// the image that went in.
/// </remarks>
[TestFixture]
public class SgiOptimizerTests {

  /// <summary>An image whose channels are all equal is a grey one written three times over.</summary>
  [Test]
  public async Task A_Grey_Image_Stored_As_Rgb_Loses_Two_Of_Its_Channels() {
    var source = _Grey24(64, 64);

    var result = await new SgiOptimizer(source).OptimizeAsync();

    Assert.Multiple(() => {
      Assert.That(result.Combo.Channels, Is.EqualTo(1), "three equal channels collapse to one");
      _AssertSamePicture(source, result.FileContents);
    });
  }

  /// <summary>A fully opaque alpha channel says nothing, so it need not be stored.</summary>
  [Test]
  public async Task An_Opaque_Alpha_Channel_Is_Dropped() {
    var source = _OpaqueRgba(32, 32);

    var result = await new SgiOptimizer(source).OptimizeAsync();

    Assert.Multiple(() => {
      Assert.That(result.Combo.Channels, Is.EqualTo(3), "opaque alpha carries nothing");
      _AssertSamePicture(source, result.FileContents);
    });
  }

  /// <summary>
  /// A picture with real colour in it keeps all three channels, however much that costs.
  /// </summary>
  /// <remarks>
  /// The guard on the reduction: it is offered only where it is provably reversible, so an image that
  /// is not grey must come back with its channels intact rather than flattened.
  /// </remarks>
  [Test]
  public async Task A_Colour_Image_Keeps_Its_Channels() {
    var source = _Colour24(32, 32);

    var result = await new SgiOptimizer(source).OptimizeAsync();

    Assert.Multiple(() => {
      Assert.That(result.Combo.Channels, Is.EqualTo(3));
      _AssertSamePicture(source, result.FileContents);
    });
  }

  /// <summary>Flat runs compress; the optimizer should notice and take the smaller encoding.</summary>
  [Test]
  public async Task A_Flat_Image_Comes_Out_Run_Length_Encoded() {
    var source = _Grey24(128, 128);

    var result = await new SgiOptimizer(source).OptimizeAsync();
    var uncompressed = SgiWriter.ToBytes(SgiFile.FromRawImage(source) with { Compression = SgiCompression.None });

    Assert.Multiple(() => {
      Assert.That(result.Combo.Compression, Is.EqualTo(SgiCompression.Rle));
      Assert.That(result.CompressedSize, Is.LessThan(uncompressed.LongLength), "and it is smaller for it");
      _AssertSamePicture(source, result.FileContents);
    });
  }

  /// <summary>Sixteen-bit samples that only ever repeat a byte fit in eight without losing anything.</summary>
  [Test]
  public async Task Sixteen_Bit_Samples_That_Fit_In_Eight_Are_Narrowed() {
    var source = _Promoted48(32, 32);

    var result = await new SgiOptimizer(source).OptimizeAsync();

    Assert.That(result.Combo.BytesPerChannel, Is.EqualTo(1));
  }

  /// <summary>Turning every choice off leaves the file as it would have been written anyway.</summary>
  [Test]
  public async Task With_Every_Reduction_Disabled_Nothing_Is_Reduced() {
    var source = _Grey24(32, 32);
    var options = new SgiOptimizationOptions(
      Compressions: [SgiCompression.None], ReduceChannels: false, ReduceDepth: false, DropImageName: false);

    var result = await new SgiOptimizer(source, options).OptimizeAsync();

    Assert.Multiple(() => {
      Assert.That(result.Combo.Channels, Is.EqualTo(3));
      Assert.That(result.Combo.Compression, Is.EqualTo(SgiCompression.None));
      _AssertSamePicture(source, result.FileContents);
    });
  }

  /// <summary>Decodes the optimizer's output and holds it against the image that went in.</summary>
  private static void _AssertSamePicture(RawImage source, byte[] encoded) {
    var decoded = SgiFile.ToRawImage(SgiReader.FromBytes(encoded));
    var expected = source.ToRgb24();
    var actual = decoded.ToRgb24();

    Assert.That(decoded.Width, Is.EqualTo(source.Width), "width");
    Assert.That(decoded.Height, Is.EqualTo(source.Height), "height");
    Assert.That(actual.Length, Is.EqualTo(expected.Length), "pixel count");
    for (var i = 0; i < expected.Length; ++i)
      if (expected[i] != actual[i])
        Assert.Fail($"pixel byte {i} changed: expected {expected[i]}, got {actual[i]}");
  }

  private static RawImage _Grey24(int width, int height) {
    var pixels = new byte[width * height * 3];
    for (var i = 0; i < width * height; ++i) {
      var level = (byte)(i / width * 2 % 256);
      pixels[i * 3] = pixels[(i * 3) + 1] = pixels[(i * 3) + 2] = level;
    }

    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = pixels };
  }

  private static RawImage _Colour24(int width, int height) {
    var pixels = new byte[width * height * 3];
    for (var i = 0; i < width * height; ++i) {
      pixels[i * 3] = (byte)(i % 251);
      pixels[(i * 3) + 1] = (byte)(i % 97);
      pixels[(i * 3) + 2] = (byte)(i % 13);
    }

    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = pixels };
  }

  private static RawImage _OpaqueRgba(int width, int height) {
    var pixels = new byte[width * height * 4];
    for (var i = 0; i < width * height; ++i) {
      pixels[i * 4] = (byte)(i % 251);
      pixels[(i * 4) + 1] = (byte)(i % 97);
      pixels[(i * 4) + 2] = (byte)(i % 13);
      pixels[(i * 4) + 3] = 0xFF;
    }

    return new() { Width = width, Height = height, Format = PixelFormat.Rgba32, PixelData = pixels };
  }

  /// <summary>Rgb48 whose samples were promoted from eight bits, so each repeats its byte.</summary>
  private static RawImage _Promoted48(int width, int height) {
    var pixels = new byte[width * height * 6];
    for (var i = 0; i < width * height * 3; ++i) {
      var value = (byte)(i % 211);
      pixels[i * 2] = value;
      pixels[(i * 2) + 1] = value;
    }

    return new() { Width = width, Height = height, Format = PixelFormat.Rgb48, PixelData = pixels };
  }
}
