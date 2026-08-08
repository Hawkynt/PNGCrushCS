using FileFormat.Core;

namespace Hawkynt.FileFormats.Images.Tests;

[TestFixture]
public sealed class SampleToTests {

  [Test]
  [Category("Unit")]
  public void AVeryWidePictureDoesNotOverflow() {
    // The arithmetic was in int, so a source wider than about 32768 wrapped negative part way along
    // a row and threw. Several headers here state a width up to 65535, so the widest picture the
    // format allows was the one that could not be sampled.
    var source = new RawImage {
      Width = 40000, Height = 2, Format = PixelFormat.Rgb24, PixelData = new byte[40000 * 2 * 3],
    };

    var sampled = source.SampleTo(320, 200);

    Assert.That((sampled.Width, sampled.Height), Is.EqualTo((320, 200)));
  }
}
