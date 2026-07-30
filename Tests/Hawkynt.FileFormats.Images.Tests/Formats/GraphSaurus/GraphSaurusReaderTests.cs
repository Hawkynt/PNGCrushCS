using System;
using System.IO;
using FileFormat.GraphSaurus;

namespace FileFormat.GraphSaurus.Tests;

[TestFixture]
public sealed class GraphSaurusReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => GraphSaurusReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => GraphSaurusReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".grs"));
    Assert.Throws<FileNotFoundException>(() => GraphSaurusReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => GraphSaurusReader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    Assert.Throws<InvalidDataException>(() => GraphSaurusReader.FromBytes(new byte[1]));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_WrongSize_ThrowsInvalidDataException() {
    Assert.Throws<InvalidDataException>(() => GraphSaurusReader.FromBytes(new byte[54273]));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ExactSize_Succeeds() {
    var data = new byte[54272];
    data[0] = 0xAB;
    data[54271] = 0xCD;

    var result = GraphSaurusReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(256));
    Assert.That(result.Height, Is.EqualTo(212));
    Assert.That(result.PixelData.Length, Is.EqualTo(54272));
    Assert.That(result.PixelData[0], Is.EqualTo(0xAB));
    Assert.That(result.PixelData[54271], Is.EqualTo(0xCD));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Valid() {
    var data = new byte[54272];
    data[0] = 0x42;

    using var ms = new MemoryStream(data);
    var result = GraphSaurusReader.FromStream(ms);

    Assert.That(result.PixelData[0], Is.EqualTo(0x42));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_CopiesData_NotReference() {
    var data = new byte[54272];
    data[0] = 0xFF;

    var result = GraphSaurusReader.FromBytes(data);
    data[0] = 0x00;

    Assert.That(result.PixelData[0], Is.EqualTo(0xFF));
  }
}
