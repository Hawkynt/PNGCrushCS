using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.Avs;

namespace FileFormat.Avs.Tests;

[TestFixture]
public sealed class AvsReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => AvsReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => AvsReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".avs"));
    Assert.Throws<FileNotFoundException>(() => AvsReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => AvsReader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    var tooSmall = new byte[4];
    Assert.Throws<InvalidDataException>(() => AvsReader.FromBytes(tooSmall));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_WrongSize_ThrowsInvalidDataException() {
    // Header says 2x2 (16 pixel bytes expected) but we only provide 12 pixel bytes
    var data = new byte[8 + 12];
    BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(0), 2);
    BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(4), 2);
    Assert.Throws<InvalidDataException>(() => AvsReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ZeroDimensions_ThrowsInvalidDataException() {
    var data = new byte[8];
    BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(0), 0);
    BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(4), 1);
    Assert.Throws<InvalidDataException>(() => AvsReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_Valid() {
    // 2x1 image: 2 pixels, 8 pixel bytes
    var data = new byte[8 + 8];
    BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(0), 2);
    BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(4), 1);
    data[8] = 0xFF;  // A
    data[9] = 0xAA;  // R
    data[10] = 0xBB; // G
    data[11] = 0xCC; // B
    data[12] = 0x80; // A
    data[13] = 0x11; // R
    data[14] = 0x22; // G
    data[15] = 0x33; // B

    var result = AvsReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(2));
    Assert.That(result.Height, Is.EqualTo(1));
    Assert.That(result.PixelData.Length, Is.EqualTo(8));
    Assert.That(result.PixelData[0], Is.EqualTo(0xFF));
    Assert.That(result.PixelData[4], Is.EqualTo(0x80));
  }
}
