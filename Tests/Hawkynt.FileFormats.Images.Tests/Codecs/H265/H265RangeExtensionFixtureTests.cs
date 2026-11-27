using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FileFormat.Core;

namespace FileFormat.Codecs.H265.Tests;

/// <summary>
/// The format range extension tools, each decoded from a stream that uses it and held to the HM
/// reference decoder.
/// </summary>
/// <remarks>
/// The streams are the small end of the JCT-VC RExt conformance suite and four the HM encoder wrote
/// for the tools that suite never combines with anything — weighted prediction at high precision,
/// chroma quantiser offset indices, lossless coding units and inter prediction above twelve bits.
/// <c>Fixtures/README.md</c> says where each came from. Two oracles apply to every stream: the
/// decoded picture hash its encoder wrote for each picture, and the MD5 of the file the HM decoder
/// writes for the whole stream. The full suite is <see cref="H265RangeExtensionConformanceTests"/>.
/// </remarks>
[TestFixture]
public sealed class H265RangeExtensionFixtureTests {

  private static string _Directory => Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", "H265");

  private static IEnumerable<TestCaseData> _Fixtures() {
    var manifest = Path.Combine(AppContext.BaseDirectory, "Fixtures", "H265", "expected.md5");
    foreach (var line in File.ReadLines(manifest)) {
      var fields = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
      if (fields.Length == 2)
        yield return new TestCaseData(fields[1], fields[0]).SetName($"RExt fixture {fields[1]}");
    }
  }

  private static byte[] _Read(string name) {
    var path = Path.Combine(_Directory, name);
    Assert.That(File.Exists(path), Is.True, $"Test fixture missing: {path}");
    return File.ReadAllBytes(path);
  }

  [TestCaseSource(nameof(_Fixtures))]
  [Category("Unit")]
  public void DecodesAsTheReferenceDecoderDoes(string name, string expectedMd5) {
    var result = H265ConformanceDecoder.Decode(_Read(name));

    Assert.Multiple(() => {
      Assert.That(result.FirstMismatch, Is.Null, "a decoded picture hash SEI message disagrees");
      Assert.That(result.MatchingPictures, Is.EqualTo(result.HashedPictures));
      Assert.That(result.OutputPictures, Is.GreaterThan(0));
      Assert.That(result.OutputMd5, Is.EqualTo(expectedMd5), "the output differs from the HM decoder's");
    });
  }

  /// <summary>
  /// Sixteen-bit 4:4:4 pictures leave the stream decoder in the sixteen-bit planar format, sample for
  /// sample.
  /// </summary>
  [Test]
  [Category("Unit")]
  public void SixteenBitPicturesComeOutAsSixteenBitPlanar() {
    var images = _DecodeToPlanar("wp_ep_16b_444.bit", out var pictures);

    Assert.That(images, Has.Count.EqualTo(9));
    var first = images[0];
    var (picture, sps) = pictures[0];
    Assert.Multiple(() => {
      Assert.That(first.Format, Is.EqualTo(PixelFormat.Yuv444P16));
      Assert.That(first.Width, Is.EqualTo(128));
      Assert.That(first.Height, Is.EqualTo(96));
      Assert.That(first.PixelData, Has.Length.EqualTo(128 * 96 * 3 * 2));
      Assert.That(sps.BitDepthLuma, Is.EqualTo(16));
      Assert.That(BitConverter.ToUInt16(first.PixelData, 2 * (128 * 96 + 5)), Is.EqualTo(picture.Cb[5]));
    });
  }

  /// <summary>
  /// A sequence whose luma and chroma depths differ comes out at the deeper of the two, the
  /// shallower plane shifted up rather than labelled as something it is not.
  /// </summary>
  [Test]
  [Category("Unit")]
  public void MixedDepthsComeOutAtTheDeeperDepth() {
    var images = _DecodeToPlanar("Bitdepth_A_RExt_Sony_1.bit", out var pictures);
    var (picture, sps) = pictures[0];
    var image = images[0];
    var lumaSamples = image.Width * image.Height;

    Assert.Multiple(() => {
      Assert.That(sps.BitDepthLuma, Is.EqualTo(12));
      Assert.That(sps.BitDepthChroma, Is.EqualTo(8));
      Assert.That(image.Format, Is.EqualTo(PixelFormat.Yuv444P12));
      Assert.That(BitConverter.ToUInt16(image.PixelData, 0), Is.EqualTo(picture.Luma[0]));
      Assert.That(BitConverter.ToUInt16(image.PixelData, 2 * lumaSamples), Is.EqualTo(picture.Cb[0] << 4));
    });
  }

  /// <summary>
  /// A monochrome sequence comes out as 4:2:0 with neutral chrominance at its own depth, which is
  /// what a reference decoder writes when asked for a 4:2:0 file.
  /// </summary>
  [Test]
  [Category("Unit")]
  public void MonochromeComesOutWithNeutralChroma() {
    var images = _DecodeToPlanar("GENERAL_16b_400_RExt_Sony_1.cut1.bit", out var pictures);
    var (picture, _) = pictures[0];
    var image = images[0];
    var lumaSamples = image.Width * image.Height;

    Assert.Multiple(() => {
      Assert.That(image.Format, Is.EqualTo(PixelFormat.Yuv420P16));
      Assert.That(BitConverter.ToUInt16(image.PixelData, 2 * 7), Is.EqualTo(picture.Luma[7]));
      Assert.That(BitConverter.ToUInt16(image.PixelData, 2 * lumaSamples), Is.EqualTo(0x8000));
    });
  }

  private static List<RawImage> _DecodeToPlanar(
    string name, out List<(H265Picture Picture, H265SequenceParameterSet Sps)> pictures) {
    var decoder = new H265StreamDecoder();
    decoder.Decode(H265NalReader.SplitAnnexB(_Read(name)));
    decoder.Flush();

    pictures = [];
    var images = new List<RawImage>();
    while (decoder.TryTakePicture(out var picture, out var sps)) {
      pictures.Add((picture, sps));
      images.Add(H265StreamDecoder.ToPlanarImage(picture, sps));
    }

    Assert.That(images, Is.Not.Empty);
    return images;
  }
}
