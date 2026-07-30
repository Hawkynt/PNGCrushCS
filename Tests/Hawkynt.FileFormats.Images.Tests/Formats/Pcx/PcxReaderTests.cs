using System;
using System.IO;
using FileFormat.Pcx;

namespace FileFormat.Pcx.Tests;

[TestFixture]
public sealed class PcxReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => PcxReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => PcxReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var file = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".pcx"));
    Assert.Throws<FileNotFoundException>(() => PcxReader.FromFile(file));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    var data = new byte[64];
    Assert.Throws<InvalidDataException>(() => PcxReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_InvalidManufacturer_ThrowsInvalidDataException() {
    var data = new byte[128];
    data[0] = 0xFF; // Invalid manufacturer (must be 0x0A)
    Assert.Throws<InvalidDataException>(() => PcxReader.FromBytes(data));
  }
}
