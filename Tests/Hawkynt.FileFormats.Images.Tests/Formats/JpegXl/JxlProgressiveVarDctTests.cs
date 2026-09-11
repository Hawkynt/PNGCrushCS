using System;
using FileFormat.JpegXl.Codec;

namespace FileFormat.JpegXl.Tests;

/// <summary>Interoperability coverage for multi-pass VarDCT AC coding.</summary>
[TestFixture]
public sealed class JxlProgressiveVarDctTests {

  [TestCase("libjxl_progressive_ac_64x48.jxl.b64")]
  [TestCase("libjxl_qprogressive_ac_64x48.jxl.b64")]
  public void ProgressiveAcFrameDecodesAllPasses(string fixture) {
    var bytes = _Fixture(fixture);

    Assert.That(JpegXlReader.TryReadSpecMetadata(bytes, out var metadata), Is.True);
    Assert.Multiple(() => {
      Assert.That(metadata.Width, Is.EqualTo(64));
      Assert.That(metadata.Height, Is.EqualTo(48));
      Assert.That(metadata.IsProgressiveFrame, Is.True);
      Assert.That(metadata.IsModularFrame, Is.False);
    });

    Assert.That(JpegXlReader.TryReadSpecImage(bytes, out _, out var raw), Is.True);
    Assert.That(raw, Is.InstanceOf<JxlVarDctImage>());
    var image = (JxlVarDctImage)raw!;
    Assert.Multiple(() => {
      Assert.That(image.Width, Is.EqualTo(64));
      Assert.That(image.Height, Is.EqualTo(48));
      Assert.That(image.Channels, Has.Length.GreaterThanOrEqualTo(3));
      Assert.That(image.Channels[0], Has.Length.EqualTo(64 * 48));
      Assert.That(image.Channels[1], Has.Length.EqualTo(64 * 48));
      Assert.That(image.Channels[2], Has.Length.EqualTo(64 * 48));
    });
  }

  [Test]
  public void QuantizedProgressiveHeaderRetainsNonZeroPassShifts() {
    var bytes = _Fixture("libjxl_qprogressive_ac_64x48.jxl.b64");
    var reader = new JxlBitReader(bytes, 2);
    var (width, height) = JxlSizeHeader.Decode(reader);
    var image = JxlImageMetadata.Decode(reader);
    JxlCustomTransformData.Decode(reader, image.XybEncoded);
    reader.ZeroPadToByte();

    var frame = JxlSpecFrameHeader.Decode(reader, image, width, height);
    Assert.Multiple(() => {
      Assert.That(frame.NumPasses, Is.GreaterThan(1));
      Assert.That(frame.PassShifts, Has.Length.EqualTo((int)frame.NumPasses));
      Assert.That(frame.PassShifts[^1], Is.Zero);
      Assert.That(frame.PassShifts, Has.Some.GreaterThan(0u));
    });
  }

  private static byte[] _Fixture(string name)
    => Convert.FromBase64String(System.Text.Encoding.ASCII.GetString(TestHelper.Fixture(name)));
}
