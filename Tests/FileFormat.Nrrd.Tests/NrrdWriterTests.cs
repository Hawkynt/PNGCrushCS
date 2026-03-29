using System;
using System.Text;
using FileFormat.Nrrd;

namespace FileFormat.Nrrd.Tests;

[TestFixture]
public sealed class NrrdWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => NrrdWriter.ToBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_StartsWithNrrdMagic() {
    var file = new NrrdFile {
      Sizes = [4, 3],
      DataType = NrrdType.UInt8,
      Encoding = NrrdEncoding.Raw,
      PixelData = new byte[12]
    };

    var bytes = NrrdWriter.ToBytes(file);
    var header = Encoding.ASCII.GetString(bytes, 0, 8);

    Assert.That(header, Is.EqualTo("NRRD0004"));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ContainsTypeField() {
    var file = new NrrdFile {
      Sizes = [2, 2],
      DataType = NrrdType.Float,
      Encoding = NrrdEncoding.Raw,
      PixelData = new byte[16]
    };

    var bytes = NrrdWriter.ToBytes(file);
    var text = Encoding.ASCII.GetString(bytes);

    Assert.That(text, Does.Contain("type: float"));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ContainsSizesField() {
    var file = new NrrdFile {
      Sizes = [10, 20],
      DataType = NrrdType.UInt8,
      Encoding = NrrdEncoding.Raw,
      PixelData = new byte[200]
    };

    var bytes = NrrdWriter.ToBytes(file);
    var text = Encoding.ASCII.GetString(bytes);

    Assert.That(text, Does.Contain("sizes: 10 20"));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ContainsDimensionField() {
    var file = new NrrdFile {
      Sizes = [5, 6, 7],
      DataType = NrrdType.UInt8,
      Encoding = NrrdEncoding.Raw,
      PixelData = new byte[210]
    };

    var bytes = NrrdWriter.ToBytes(file);
    var text = Encoding.ASCII.GetString(bytes);

    Assert.That(text, Does.Contain("dimension: 3"));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ContainsEncodingField() {
    var file = new NrrdFile {
      Sizes = [2],
      DataType = NrrdType.UInt8,
      Encoding = NrrdEncoding.Gzip,
      PixelData = new byte[2]
    };

    var bytes = NrrdWriter.ToBytes(file);
    var text = Encoding.ASCII.GetString(bytes, 0, Math.Min(bytes.Length, 200));

    Assert.That(text, Does.Contain("encoding: gzip"));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ContainsEndianField() {
    var file = new NrrdFile {
      Sizes = [2],
      DataType = NrrdType.UInt8,
      Encoding = NrrdEncoding.Raw,
      Endian = "big",
      PixelData = new byte[2]
    };

    var bytes = NrrdWriter.ToBytes(file);
    var text = Encoding.ASCII.GetString(bytes);

    Assert.That(text, Does.Contain("endian: big"));
  }
}
