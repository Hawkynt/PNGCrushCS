using System;
using System.Linq;
using FileFormat.Avif;
using FileFormat.Avif.Codec;
using FileFormat.Core;

namespace FileFormat.Avif.Tests;

/// <summary>
/// What the writer produces is a real AVIF: an ISO base media file whose primary item is an AV1
/// key frame, lossless, so the picture that comes back is the picture that went in.
/// </summary>
[TestFixture]
public sealed class AvifEncoderTests {

  private static byte[] _Picture(int width, int height, int seed) {
    var pixels = new byte[width * height * 3];
    var random = new Random(seed);

    // A smooth field with occasional edges: enough structure to reach several coefficient contexts
    // without being so noisy that nothing predicts.
    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x) {
        var index = (y * width + x) * 3;
        pixels[index] = (byte)((x * 7 + y * 3 + random.Next(8)) & 0xFF);
        pixels[index + 1] = (byte)((x * 3 + y * 11) & 0xFF);
        pixels[index + 2] = (byte)(((x ^ y) * 5 + random.Next(4)) & 0xFF);
      }

    return pixels;
  }

  [TestCase(1, 1)]
  [TestCase(3, 7)]
  [TestCase(8, 8)]
  [TestCase(17, 5)]
  [TestCase(33, 17)]
  [TestCase(64, 64)]
  [TestCase(64, 48)]
  [TestCase(65, 65)]
  [TestCase(96, 80)]
  [TestCase(129, 97)]
  [Category("Integration")]
  public void APictureSurvivesTheWriterAndReaderUnchanged(int width, int height) {
    var pixels = _Picture(width, height, width * 31 + height);
    var written = AvifWriter.ToBytes(new() { Width = width, Height = height, PixelData = pixels });
    var restored = AvifReader.FromBytes(written);

    Assert.Multiple(() => {
      Assert.That(restored.Width, Is.EqualTo(width));
      Assert.That(restored.Height, Is.EqualTo(height));
      Assert.That(restored.HasAlpha, Is.False);
      Assert.That(restored.PixelData, Is.EqualTo(pixels));
    });
  }

  /// <summary>
  /// Noise is the hard case for the coefficient coder: it drives levels past the base range into
  /// the Golomb suffix and fills every end-of-block class.
  /// </summary>
  [Test]
  [Category("Integration")]
  public void ANoiseFieldSurvivesUnchanged() {
    const int WIDTH = 48;
    const int HEIGHT = 40;
    var pixels = new byte[WIDTH * HEIGHT * 3];
    new Random(4711).NextBytes(pixels);

    var written = AvifWriter.ToBytes(new() { Width = WIDTH, Height = HEIGHT, PixelData = pixels });
    var restored = AvifReader.FromBytes(written);

    Assert.That(restored.PixelData, Is.EqualTo(pixels));
  }

  /// <summary>A picture wide enough to force more than one tile still round-trips.</summary>
  [Test]
  [Category("Integration")]
  public void APictureWiderThanOneTileIsSplitAndStillRoundTrips() {
    const int WIDTH = 4200;
    const int HEIGHT = 8;
    var pixels = _Picture(WIDTH, HEIGHT, 5);

    var written = AvifWriter.ToBytes(new() { Width = WIDTH, Height = HEIGHT, PixelData = pixels });

    var layout = AvifItemLayout.Parse(written);
    var coded = layout.GetItemData(written, layout.PrimaryItemId);
    var decoded = Av1FrameDecoder.DecodeToPlanes(coded, 0, coded.Length);

    Assert.Multiple(() => {
      Assert.That(decoded.Frame.TileCols, Is.GreaterThan(1), "4200 samples exceed the maximum tile width");
      Assert.That(AvifReader.FromBytes(written).PixelData, Is.EqualTo(pixels));
    });
  }

  /// <summary>
  /// The container has to carry the properties AVIF requires. A file without <c>av1C</c> is not
  /// AVIF whatever its extension says, and nothing else will open it.
  /// </summary>
  [Test]
  [Category("Unit")]
  public void TheWrittenContainerCarriesThePropertiesAvifRequires() {
    var written = AvifWriter.ToBytes(new() { Width = 20, Height = 12, PixelData = _Picture(20, 12, 1) });
    var layout = AvifItemLayout.Parse(written);
    var primary = layout.PrimaryItemId;

    var av1C = layout.GetProperty(primary, IsoBmffBox.Av1C);
    var ispe = layout.GetProperty(primary, IsoBmffBox.Ispe);
    var pixi = layout.GetProperty(primary, IsoBmffBox.Pixi);
    var colr = layout.GetProperty(primary, IsoBmffBox.Colr);

    Assert.Multiple(() => {
      Assert.That(layout.ItemTypes[primary], Is.EqualTo("av01"));
      Assert.That(av1C, Is.Not.Null);
      Assert.That(av1C![0], Is.EqualTo(0x81), "av1C marker and version");
      Assert.That(av1C[1] >> 5, Is.EqualTo(1), "seq_profile must match the bitstream's 4:4:4 profile");
      Assert.That(ispe, Is.Not.Null);
      Assert.That(pixi, Is.Not.Null);
      Assert.That(colr, Is.Not.Null);
      Assert.That(layout.ItemExtents[primary], Has.Count.EqualTo(1));
    });
  }

  /// <summary>
  /// The av1C property repeats the sequence header's profile and colour layout. A reader is
  /// entitled to trust it without decoding, so it has to agree with what was actually coded.
  /// </summary>
  [Test]
  [Category("Unit")]
  public void TheCodecConfigurationAgreesWithTheBitstream() {
    var written = AvifWriter.ToBytes(new() { Width = 40, Height = 24, PixelData = _Picture(40, 24, 2) });
    var layout = AvifItemLayout.Parse(written);
    var primary = layout.PrimaryItemId;
    var av1C = layout.GetProperty(primary, IsoBmffBox.Av1C)!;
    var coded = layout.GetItemData(written, primary);
    var decoded = Av1FrameDecoder.DecodeToPlanes(coded, 0, coded.Length);
    var sequence = decoded.Sequence;

    Assert.Multiple(() => {
      Assert.That(av1C[1] >> 5, Is.EqualTo(sequence.SeqProfile));
      Assert.That((av1C[2] >> 6) & 1, Is.EqualTo(sequence.BitDepth > 8 ? 1 : 0), "high_bitdepth");
      Assert.That((av1C[2] >> 4) & 1, Is.EqualTo(sequence.MonoChrome ? 1 : 0), "monochrome");
      Assert.That((av1C[2] >> 3) & 1, Is.EqualTo(sequence.SubsamplingX), "chroma_subsampling_x");
      Assert.That((av1C[2] >> 2) & 1, Is.EqualTo(sequence.SubsamplingY), "chroma_subsampling_y");
      Assert.That(sequence.MatrixCoefficients, Is.EqualTo(Av1MatrixCoefficients.Identity));
      Assert.That(decoded.Frame.CodedLossless, Is.True);
    });
  }

  [Test]
  [Category("Integration")]
  public void TheRegistryPathRoundTripsARawImage() {
    var pixels = _Picture(24, 18, 9);
    var image = new RawImage { Width = 24, Height = 18, Format = PixelFormat.Rgb24, PixelData = pixels };

    var written = AvifWriter.ToBytes(AvifFile.FromRawImage(image));
    var restored = AvifFile.ToRawImage(AvifReader.FromBytes(written));

    Assert.Multiple(() => {
      Assert.That(restored.Format, Is.EqualTo(PixelFormat.Rgb24));
      Assert.That(restored.Width, Is.EqualTo(24));
      Assert.That(restored.Height, Is.EqualTo(18));
      Assert.That(restored.PixelData, Is.EqualTo(pixels));
    });
  }

  /// <summary>An alpha channel is refused rather than silently dropped.</summary>
  [Test]
  [Category("Unit")]
  public void AnAlphaChannelIsRefusedByName() {
    var file = new AvifFile { Width = 4, Height = 4, HasAlpha = true, PixelData = new byte[4 * 4 * 4] };

    Assert.That(() => AvifWriter.ToBytes(file), Throws.InstanceOf<NotSupportedException>());
  }
}
