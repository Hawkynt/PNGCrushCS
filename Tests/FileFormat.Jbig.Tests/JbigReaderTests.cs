using System;
using System.IO;
using FileFormat.Jbig;

namespace FileFormat.Jbig.Tests;

[TestFixture]
public sealed class JbigReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => JbigReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => JbigReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".jbg"));
    Assert.Throws<FileNotFoundException>(() => JbigReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => JbigReader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    var tooSmall = new byte[10];
    Assert.Throws<InvalidDataException>(() => JbigReader.FromBytes(tooSmall));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_InvalidDifferentialLayers_ThrowsInvalidDataException() {
    var data = _CreateMinimalHeader(8, 1, d: 2);
    Assert.Throws<InvalidDataException>(() => JbigReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_InvalidPlanes_ThrowsInvalidDataException() {
    var data = _CreateMinimalHeader(8, 1, p: 3);
    Assert.Throws<InvalidDataException>(() => JbigReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidHeader_ParsesDimensions() {
    var original = new JbigFile {
      Width = 16,
      Height = 4,
      PixelData = new byte[2 * 4]
    };

    var bytes = JbigWriter.ToBytes(original);
    var result = JbigReader.FromBytes(bytes);

    Assert.That(result.Width, Is.EqualTo(16));
    Assert.That(result.Height, Is.EqualTo(4));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_ValidData_ParsesCorrectly() {
    var original = new JbigFile {
      Width = 8,
      Height = 2,
      PixelData = new byte[2]
    };

    var bytes = JbigWriter.ToBytes(original);
    using var ms = new MemoryStream(bytes);
    var result = JbigReader.FromStream(ms);

    Assert.That(result.Width, Is.EqualTo(8));
    Assert.That(result.Height, Is.EqualTo(2));
  }

  private static byte[] _CreateMinimalHeader(int width, int height, byte d = 0, byte p = 1) {
    var header = new JbigHeader(
      DL: 0,
      D: d,
      P: p,
      Reserved: 0,
      XD: width,
      YD: height,
      L0: height,
      MX: 8,
      MY: 0,
      Options: 0,
      Order: 0
    );

    var data = new byte[JbigHeader.StructSize + 8]; // header + some padding
    header.WriteTo(data.AsSpan());
    return data;
  }
}
