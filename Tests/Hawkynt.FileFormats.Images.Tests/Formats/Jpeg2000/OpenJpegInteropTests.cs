using System;
using System.IO;
using System.Text;
using FileFormat.Jpeg2000;

namespace FileFormat.Jpeg2000.Tests;

/// <summary>Codestreams written by OpenJPEG, decoded here and checked against OpenJPEG's own decode.</summary>
/// <remarks>
/// Every other JPEG 2000 test in this suite goes through this project's own writer, so a reader and
/// a writer that agree only with each other pass all of them. These do not: the fixtures were
/// produced by <c>opj_compress</c> from the two pictures committed beside them, one file per coding
/// option that changes the shape of the packet stream — decomposition depth, tiles, precincts, a
/// position-led progression, SOP and EPH markers, several quality layers, the colour transform on
/// and off, and the irreversible filter.
///
/// <para>For the lossless files <c>opj_decompress</c> returns the source picture unchanged, so the
/// source picture is the reference and the comparison is equality. The irreversible file is compared
/// against OpenJPEG's decode of it, within the tolerance two floating-point wavelet implementations
/// differ by.</para>
/// </remarks>
[TestFixture]
public sealed class OpenJpegInteropTests {

  /// <summary>What two implementations of the 9/7 synthesis may differ by, per sample.</summary>
  private const int _IRREVERSIBLE_TOLERANCE = 2;

  private static byte[] _Fixture(string name) {
    var path = Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", "Jpeg2000", name);
    Assert.That(File.Exists(path), Is.True, $"Test fixture missing: {path}");
    return File.ReadAllBytes(path);
  }

  [Test]
  [Category("Integration")]
  public void AGreyCodestreamDecodesToWhatOpenJpegWasGiven()
    => _AssertExact("grey-default.j2k", "grey.pgm");

  [TestCase("colour-default.j2k", TestName = "Default coding options")]
  [TestCase("colour-default.jp2", TestName = "The same codestream in a JP2 container")]
  [TestCase("colour-nomct.j2k", TestName = "Without the component transform")]
  [TestCase("colour-tiles.j2k", TestName = "Split into 40 by 40 tiles")]
  [TestCase("colour-precincts.j2k", TestName = "With an explicit precinct partition")]
  [TestCase("colour-rpcl.j2k", TestName = "In resolution-position-component-layer order")]
  [TestCase("colour-sopeph.j2k", TestName = "With SOP and EPH markers")]
  [TestCase("colour-layers.j2k", TestName = "Across three quality layers")]
  [Category("Integration")]
  public void AColourCodestreamDecodesToWhatOpenJpegWasGiven(string fixture)
    => _AssertExact(fixture, "colour.ppm");

  [Test]
  [Category("Integration")]
  public void AnIrreversibleCodestreamDecodesToWhatOpenJpegDecodesItTo() {
    var image = Jpeg2000Reader.FromBytes(_Fixture("colour-lossy97.j2k"));
    var (width, height, components, expected) = _ReadPnm(_Fixture("colour-lossy97.ppm"));

    Assert.Multiple(() => {
      Assert.That(image.Width, Is.EqualTo(width));
      Assert.That(image.Height, Is.EqualTo(height));
      Assert.That(image.ComponentCount, Is.EqualTo(components));
    });

    var worst = 0;
    var at = -1;
    for (var i = 0; i < expected.Length; ++i) {
      var delta = Math.Abs(image.PixelData[i] - expected[i]);
      if (delta <= worst)
        continue;

      worst = delta;
      at = i;
    }

    Assert.That(worst, Is.LessThanOrEqualTo(_IRREVERSIBLE_TOLERANCE),
      $"sample {at} is {(at < 0 ? 0 : image.PixelData[at])} where OpenJPEG decodes {(at < 0 ? 0 : expected[at])}");
  }

  private static void _AssertExact(string fixture, string reference) {
    var image = Jpeg2000Reader.FromBytes(_Fixture(fixture));
    var (width, height, components, expected) = _ReadPnm(_Fixture(reference));

    Assert.Multiple(() => {
      Assert.That(image.Width, Is.EqualTo(width));
      Assert.That(image.Height, Is.EqualTo(height));
      Assert.That(image.ComponentCount, Is.EqualTo(components));
    });

    var differing = 0;
    var at = -1;
    for (var i = 0; i < expected.Length; ++i) {
      if (image.PixelData[i] == expected[i])
        continue;

      ++differing;
      if (at < 0)
        at = i;
    }

    Assert.That(differing, Is.Zero,
      $"{differing} of {expected.Length} samples differ, first at {at}: "
      + $"{(at < 0 ? 0 : image.PixelData[at])} where the source has {(at < 0 ? 0 : expected[at])}");
  }

  private static (int Width, int Height, int Components, byte[] Pixels) _ReadPnm(byte[] pnm) {
    var at = 0;
    string Token() {
      while (at < pnm.Length && char.IsWhiteSpace((char)pnm[at]))
        ++at;

      var start = at;
      while (at < pnm.Length && !char.IsWhiteSpace((char)pnm[at]))
        ++at;

      return Encoding.ASCII.GetString(pnm, start, at - start);
    }

    var magic = Token();
    Assert.That(magic, Is.AnyOf("P5", "P6"));
    var components = magic == "P6" ? 3 : 1;
    var width = int.Parse(Token());
    var height = int.Parse(Token());
    Assert.That(Token(), Is.EqualTo("255"));
    ++at;

    var pixels = new byte[width * height * components];
    Array.Copy(pnm, at, pixels, 0, pixels.Length);
    return (width, height, components, pixels);
  }
}
