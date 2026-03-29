using System;
using System.IO;
using FileFormat.DoodleComp;

namespace FileFormat.DoodleComp.Tests;

[TestFixture]
public sealed class DoodleCompReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => DoodleCompReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => DoodleCompReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".jj"));
    Assert.Throws<FileNotFoundException>(() => DoodleCompReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => DoodleCompReader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    Assert.Throws<InvalidDataException>(() => DoodleCompReader.FromBytes(new byte[2]));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidUncompressedParsesCorrectly() {
    // Build a valid file: 2 load address bytes + 9000 uncompressed bytes
    var data = new byte[2 + 9000];
    data[0] = 0x00;
    data[1] = 0x5C;

    var result = DoodleCompReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(320));
    Assert.That(result.Height, Is.EqualTo(200));
    Assert.That(result.LoadAddress, Is.EqualTo(0x5C00));
    Assert.That(result.BitmapData.Length, Is.EqualTo(8000));
    Assert.That(result.ScreenRam.Length, Is.EqualTo(1000));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_RleCompressed_DecompressesCorrectly() {
    using var ms = new MemoryStream();
    // Load address
    ms.WriteByte(0x00);
    ms.WriteByte(0x5C);
    // RLE: escape(0xFE), count(255), value(0xAA) = 255 bytes of 0xAA
    ms.WriteByte(0xFE);
    ms.WriteByte(255);
    ms.WriteByte(0xAA);
    // Fill rest with RLE zeros to reach 9000 decompressed bytes
    var remaining = 9000 - 255;
    while (remaining > 0) {
      var chunk = Math.Min(remaining, 255);
      ms.WriteByte(0xFE);
      ms.WriteByte((byte)chunk);
      ms.WriteByte(0x00);
      remaining -= chunk;
    }

    var result = DoodleCompReader.FromBytes(ms.ToArray());

    Assert.That(result.BitmapData[0], Is.EqualTo(0xAA));
    Assert.That(result.BitmapData[254], Is.EqualTo(0xAA));
    Assert.That(result.BitmapData[255], Is.EqualTo(0x00));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Valid() {
    var data = new byte[2 + 9000];
    data[0] = 0x00;
    data[1] = 0x5C;

    using var ms = new MemoryStream(data);
    var result = DoodleCompReader.FromStream(ms);

    Assert.That(result.Width, Is.EqualTo(320));
    Assert.That(result.Height, Is.EqualTo(200));
  }
}
