using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.Cmu;

namespace FileFormat.Cmu.Tests;

[TestFixture]
public sealed class CmuReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => CmuReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => CmuReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".cmu"));
    Assert.Throws<FileNotFoundException>(() => CmuReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => CmuReader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    var tooSmall = new byte[4];
    Assert.Throws<InvalidDataException>(() => CmuReader.FromBytes(tooSmall));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ZeroWidth_ThrowsInvalidDataException() {
    var data = new byte[8];
    BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(0), 0);
    BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(4), 1);
    Assert.Throws<InvalidDataException>(() => CmuReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ZeroHeight_ThrowsInvalidDataException() {
    var data = new byte[8];
    BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(0), 8);
    BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(4), 0);
    Assert.Throws<InvalidDataException>(() => CmuReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_Valid() {
    // 8x2 CMU: 1 byte per row, 2 rows
    var data = new byte[8 + 2];
    BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(0), 8);
    BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(4), 2);
    data[8] = 0xFF;
    data[9] = 0xAA;

    var result = CmuReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(8));
    Assert.That(result.Height, Is.EqualTo(2));
    Assert.That(result.PixelData.Length, Is.EqualTo(2));
    Assert.That(result.PixelData[0], Is.EqualTo(0xFF));
    Assert.That(result.PixelData[1], Is.EqualTo(0xAA));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Valid() {
    var data = new byte[8 + 1];
    BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(0), 8);
    BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(4), 1);
    data[8] = 0xCD;

    using var ms = new MemoryStream(data);
    var result = CmuReader.FromStream(ms);

    Assert.That(result.Width, Is.EqualTo(8));
    Assert.That(result.Height, Is.EqualTo(1));
    Assert.That(result.PixelData[0], Is.EqualTo(0xCD));
  }
}
