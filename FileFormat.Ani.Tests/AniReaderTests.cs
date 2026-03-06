using System;
using System.IO;
using FileFormat.Ani;

namespace FileFormat.Ani.Tests;

[TestFixture]
public sealed class AniReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => AniReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => AniReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var file = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".ani"));
    Assert.Throws<FileNotFoundException>(() => AniReader.FromFile(file));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    var data = new byte[8];
    Assert.Throws<InvalidDataException>(() => AniReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_InvalidFormType_ThrowsInvalidDataException() {
    // Valid RIFF header but wrong form type (not ACON)
    var data = new byte[12];
    data[0] = (byte)'R'; data[1] = (byte)'I'; data[2] = (byte)'F'; data[3] = (byte)'F';
    data[4] = 4; data[5] = 0; data[6] = 0; data[7] = 0; // size = 4
    data[8] = (byte)'W'; data[9] = (byte)'A'; data[10] = (byte)'V'; data[11] = (byte)'E';
    Assert.Throws<InvalidDataException>(() => AniReader.FromBytes(data));
  }
}
