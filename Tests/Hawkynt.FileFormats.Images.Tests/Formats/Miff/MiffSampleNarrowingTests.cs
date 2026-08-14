using System.Text;
using FileFormat.Miff;

namespace FileFormat.Miff.Tests;

/// <summary>A sample deeper than eight bits is rounded to eight, not cut.</summary>
/// <remarks>
/// ImageMagick writes sixteen bits by default and scales a sample down by the full range —
/// <c>round(sample * 255 / 65535)</c> — where taking the leading byte is a floor. The two disagree
/// by one on two fifths of a sample's values, and that is a real difference in the picture: of the
/// 2257 pixels of a 61x37 reference, 366 came back one level dark on every truecolour file this
/// project read, compressed or not.
/// <para/>
/// One level is not visible, but it is not nothing either: it is the whole distance between us and
/// the tool that wrote the file, and while it stands there is nothing left to measure a real defect
/// against.
/// </remarks>
[TestFixture]
public sealed class MiffSampleNarrowingTests {

  private static byte[] _BuildMiff(int width, int height, int depth, string type, byte[] samples) {
    var header = Encoding.ASCII.GetBytes(
      "id=ImageMagick version=1.0\n"
      + "class=DirectClass colors=0 alpha-trait=Undefined\n"
      + $"columns={width} rows={height} depth={depth}\n"
      + $"type={type}\ncolorspace=sRGB\n"
      + "\f\n:\x1a");

    var data = new byte[header.Length + samples.Length];
    header.CopyTo(data, 0);
    samples.CopyTo(data, header.Length);
    return data;
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_SixteenBitSamples_AreRounded() {
    // 0x1EFF is 30.87 of 255, 0x1E00 is 29.88, and 0xFFFF must stay white rather than wrap.
    byte[] samples = [0x1E, 0xFF, 0x1E, 0x00, 0xFF, 0xFF];
    var image = MiffFile.ToRawImage(MiffReader.FromBytes(_BuildMiff(1, 1, 16, "TrueColor", samples)));

    Assert.That(image.PixelData[..3], Is.EqualTo(new byte[] { 31, 30, 255 }));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_ThirtyTwoBitSamples_AreRounded() {
    byte[] samples = [0x1E, 0xFF, 0x00, 0x00, 0x1E, 0x00, 0x00, 0x00, 0xFF, 0xFF, 0xFF, 0xFF];
    var image = MiffFile.ToRawImage(MiffReader.FromBytes(_BuildMiff(1, 1, 32, "TrueColor", samples)));

    Assert.That(image.PixelData[..3], Is.EqualTo(new byte[] { 31, 30, 255 }));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_EightBitSamples_AreUntouched() {
    byte[] samples = [0x1E, 0x7F, 0xFF];
    var image = MiffFile.ToRawImage(MiffReader.FromBytes(_BuildMiff(1, 1, 8, "TrueColor", samples)));

    Assert.That(image.PixelData[..3], Is.EqualTo(new byte[] { 0x1E, 0x7F, 0xFF }));
  }
}
