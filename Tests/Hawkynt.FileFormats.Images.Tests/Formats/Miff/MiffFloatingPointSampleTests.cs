using System.IO;
using System.Text;
using FileFormat.Core;
using FileFormat.Miff;

namespace FileFormat.Miff.Tests;

/// <summary>
/// <c>quantum:format=floating-point</c> says the samples are IEEE floats scaled to zero..one.
/// </summary>
/// <remarks>
/// A high-dynamic-range ImageMagick writes them for anything it holds as real numbers, and
/// <c>magick src.png -colorspace Gray grey.miff</c> is one such file: <c>depth=16</c>,
/// <c>type=Grayscale</c> and half floats. Its first sample is 0x2C9F, which is 0.0722 and therefore
/// 18 of 255; read as a sixteen-bit integer its leading byte is 0x2C, which is 44. That is the whole
/// picture wrong by a curve nobody can see is a misread — measured as 688 of 2257 pixels differing
/// from what ImageMagick makes of its own file.
/// </remarks>
[TestFixture]
public sealed class MiffFloatingPointSampleTests {

  private static byte[] _BuildGrayMiff(int width, int height, int depth, string quantumFormat, byte[] samples) {
    var header = Encoding.ASCII.GetBytes(
      "id=ImageMagick version=1.0\n"
      + "class=DirectClass colors=0 alpha-trait=Undefined\n"
      + "number-channels=1 number-meta-channels=0\n"
      + $"columns={width} rows={height} depth={depth}\n"
      + "type=Grayscale\ncolorspace=Gray\n"
      + $"quantum:format={quantumFormat}\n"
      + "\f\n:\x1a");

    var data = new byte[header.Length + samples.Length];
    header.CopyTo(data, 0);
    samples.CopyTo(data, header.Length);
    return data;
  }

  /// <summary>Half floats, which is what a sixteen-bit floating-point MIFF holds.</summary>
  [Test]
  [Category("Unit")]
  public void ToRawImage_HalfFloatGrayscale_MatchesTheValuesImageMagickReads() {
    // 0x2C9F is 0.0722 and 0x3B6C is 0.9277: the darkest and lightest samples of the reference file.
    byte[] samples = [0x2C, 0x9F, 0x3B, 0x6C, 0x00, 0x00, 0x3C, 0x00];
    var image = MiffFile.ToRawImage(MiffReader.FromBytes(_BuildGrayMiff(4, 1, 16, "floating-point", samples)));

    Assert.Multiple(() => {
      Assert.That(image.Format, Is.EqualTo(PixelFormat.Gray8));
      Assert.That(image.PixelData[..4], Is.EqualTo(new byte[] { 18, 237, 0, 255 }));
    });
  }

  /// <summary>Single precision, which is what a thirty-two-bit floating-point MIFF holds.</summary>
  [Test]
  [Category("Unit")]
  public void ToRawImage_SinglePrecisionGrayscale_MatchesTheValuesImageMagickReads() {
    byte[] samples = [
      0x3F, 0x00, 0x00, 0x00, // 0.5
      0x3F, 0x80, 0x00, 0x00, // 1.0
      0x00, 0x00, 0x00, 0x00, // 0.0
      0x3E, 0x80, 0x00, 0x00, // 0.25
    ];

    var image = MiffFile.ToRawImage(MiffReader.FromBytes(_BuildGrayMiff(4, 1, 32, "floating-point", samples)));

    Assert.That(image.PixelData[..4], Is.EqualTo(new byte[] { 128, 255, 0, 64 }));
  }

  /// <summary>A sample outside zero..one is held, not wrapped.</summary>
  /// <remarks>
  /// High dynamic range is the reason this format exists, so a file may state a sample brighter than
  /// white or darker than black. Eight bits cannot hold either, and clamping is what ImageMagick
  /// does when it writes one out to a picture that cannot.
  /// </remarks>
  [Test]
  [Category("Unit")]
  public void ToRawImage_SamplesOutsideZeroToOne_AreClamped() {
    byte[] samples = [
      0x40, 0x00, 0x00, 0x00, // 2.0
      0xBF, 0x80, 0x00, 0x00, // -1.0
    ];

    var image = MiffFile.ToRawImage(MiffReader.FromBytes(_BuildGrayMiff(2, 1, 32, "floating-point", samples)));

    Assert.That(image.PixelData[..2], Is.EqualTo(new byte[] { 255, 0 }));
  }

  /// <summary>An unstated quantum format means unsigned integers, which is the ordinary file.</summary>
  [Test]
  [Category("Unit")]
  public void ToRawImage_NoQuantumFormat_ReadsUnsignedSamples() {
    byte[] samples = [0x12, 0x7C, 0xED, 0x7F];
    var image = MiffFile.ToRawImage(MiffReader.FromBytes(_BuildGrayMiff(2, 1, 16, "unsigned", samples)));

    Assert.That(image.PixelData[..2], Is.EqualTo(new byte[] { 0x12, 0xED }));
  }

  /// <summary>A sample format we cannot read is refused rather than drawn wrong.</summary>
  [Test]
  [Category("Unit")]
  public void FromBytes_SignedSamples_AreRefused() {
    byte[] samples = [0x12, 0x7C, 0xED, 0x7F];
    var failure = Assert.Throws<InvalidDataException>(() => MiffReader.FromBytes(_BuildGrayMiff(2, 1, 16, "signed", samples)));

    Assert.That(failure!.Message, Does.Contain("signed"));
  }
}
