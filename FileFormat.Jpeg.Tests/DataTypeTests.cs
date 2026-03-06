using FileFormat.Jpeg;
using NUnit.Framework;

namespace FileFormat.Jpeg.Tests;

[TestFixture]
public sealed class DataTypeTests {

  [Test]
  [Category("Unit")]
  public void JpegMode_HasExpectedValues() {
    Assert.That((int)JpegMode.Baseline, Is.EqualTo(0));
    Assert.That((int)JpegMode.Progressive, Is.EqualTo(1));
  }

  [Test]
  [Category("Unit")]
  public void JpegSubsampling_HasExpectedValues() {
    Assert.That((int)JpegSubsampling.Chroma444, Is.EqualTo(0));
    Assert.That((int)JpegSubsampling.Chroma422, Is.EqualTo(1));
    Assert.That((int)JpegSubsampling.Chroma420, Is.EqualTo(2));
  }
}
