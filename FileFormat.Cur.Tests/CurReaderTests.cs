using System;
using System.IO;
using FileFormat.Cur;

namespace FileFormat.Cur.Tests;

[TestFixture]
public sealed class CurReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => CurReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => CurReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    var data = new byte[4];
    Assert.Throws<InvalidDataException>(() => CurReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_InvalidType_ThrowsInvalidDataException() {
    var data = new byte[6];
    data[0] = 0; data[1] = 0; // reserved = 0
    data[2] = 1; data[3] = 0; // type = 1 (Icon, not Cursor)
    data[4] = 0; data[5] = 0; // count = 0
    Assert.Throws<InvalidDataException>(() => CurReader.FromBytes(data));
  }
}
