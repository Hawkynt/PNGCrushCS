using System;
using FileFormat.Core;

namespace FileFormat.Jpeg.Tests;

/// <summary>
/// The forward transform's output is scaled up by eight, and the quantiser has to divide it out.
/// </summary>
/// <remarks>
/// The integer forward transform here is libjpeg's slow-but-accurate one, which deliberately leaves
/// its output eight times larger than the coefficient it stands for so that the division which
/// follows can absorb the factor. Dividing by the table value alone wrote every coefficient eight
/// times too large.
/// <para/>
/// Our own decoder multiplied by the table value alone as well, so the two agreed exactly with each
/// other and with no other decoder in the world. Every JPEG this project wrote was wrong by a mean
/// of 31 of 255 when read by anything else, and no round trip through this project's own pair could
/// ever have shown it. That is the whole reason this fixture asserts an absolute fact about the
/// coefficients rather than comparing a decode against an encode.
/// </remarks>
[TestFixture]
public sealed class QuantizerScaleTests {

  [Test]
  [Category("Unit")]
  public void Quantizer_DividesOutTheTransformsOwnScale() {
    // A flat block of level-shifted 72 has a true DC of 8 * 72 = 576; the transform reports it as
    // 64 * 72 = 4608. Quantised by a step of 3 the coefficient is 192, not 1536.
    Assert.That(JpegQuantizer.QuantizeDctOutput(4608, 3), Is.EqualTo(192));
    Assert.That(JpegQuantizer.ForwardDctScale, Is.EqualTo(8));
  }

  [Test]
  [Category("Unit")]
  public void FlatBlock_QuantisesToTheValueItsBrightnessImplies() {
    var pixels = new byte[64];
    Array.Fill(pixels, (byte)200);

    var quant = new int[64];
    Array.Fill(quant, 3);

    var coefficients = new short[64];
    JpegDct.ForwardDctQuantize(pixels, 0, 8, quant, coefficients);

    // 200 shifts to 72, whose true DC is 576; over a step of 3 that is 192.
    Assert.That(coefficients[0], Is.EqualTo(192).Within(1));

    // A flat block has no detail, so nothing but the first coefficient may be set.
    for (var i = 1; i < 64; ++i)
      Assert.That(coefficients[i], Is.EqualTo(0), $"coefficient {i} of a flat block");
  }

  [TestCase((byte)0)]
  [TestCase((byte)64)]
  [TestCase((byte)128)]
  [TestCase((byte)200)]
  [TestCase((byte)255)]
  [Category("Integration")]
  public void RoundTrip_KeepsAFlatGreyExactly(byte value) {
    var pixels = new byte[64 * 64];
    Array.Fill(pixels, value);

    var original = new RawImage { Width = 64, Height = 64, Format = PixelFormat.Gray8, PixelData = pixels };
    var image = JpegFile.ToRawImage(JpegReader.FromSpan(
      JpegWriter.ToBytes(JpegFile.FromRawImage(original))));

    // A flat picture is the one case a lossy codec has no excuse for: it costs one coefficient.
    Assert.That(image.PixelData[0], Is.EqualTo(value).Within(2));
    Assert.That(image.PixelData[^1], Is.EqualTo(value).Within(2));
  }
}
