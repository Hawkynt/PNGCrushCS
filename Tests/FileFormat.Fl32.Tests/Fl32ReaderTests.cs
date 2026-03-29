using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.Fl32;

namespace FileFormat.Fl32.Tests;

[TestFixture]
public sealed class Fl32ReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => Fl32Reader.FromBytes(null!));

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => Fl32Reader.FromFile(null!));

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".fl32"));
    Assert.Throws<FileNotFoundException>(() => Fl32Reader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => Fl32Reader.FromStream(null!));

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    var tooSmall = new byte[8];
    Assert.Throws<InvalidDataException>(() => Fl32Reader.FromBytes(tooSmall));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_InvalidMagic_ThrowsInvalidDataException() {
    var data = new byte[16 + 4];
    BinaryPrimitives.WriteUInt32LittleEndian(data, 0xDEADBEEF);
    BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(4), 1);
    BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(8), 1);
    BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(12), 1);
    Assert.Throws<InvalidDataException>(() => Fl32Reader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_InvalidChannels_ThrowsInvalidDataException() {
    var data = new byte[16 + 8];
    BinaryPrimitives.WriteUInt32LittleEndian(data, Fl32File.Magic);
    BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(4), 1);
    BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(8), 1);
    BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(12), 2);
    Assert.Throws<InvalidDataException>(() => Fl32Reader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidGray_ParsesCorrectly() {
    var data = _BuildFl32(2, 3, 1);
    var result = Fl32Reader.FromBytes(data);

    Assert.Multiple(() => {
      Assert.That(result.Width, Is.EqualTo(2));
      Assert.That(result.Height, Is.EqualTo(3));
      Assert.That(result.Channels, Is.EqualTo(1));
      Assert.That(result.PixelData, Has.Length.EqualTo(6));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidRgb_ParsesCorrectly() {
    var data = _BuildFl32(4, 2, 3);
    var result = Fl32Reader.FromBytes(data);

    Assert.Multiple(() => {
      Assert.That(result.Width, Is.EqualTo(4));
      Assert.That(result.Height, Is.EqualTo(2));
      Assert.That(result.Channels, Is.EqualTo(3));
      Assert.That(result.PixelData, Has.Length.EqualTo(24));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidRgba_ParsesCorrectly() {
    var data = _BuildFl32(2, 2, 4);
    var result = Fl32Reader.FromBytes(data);

    Assert.Multiple(() => {
      Assert.That(result.Channels, Is.EqualTo(4));
      Assert.That(result.PixelData, Has.Length.EqualTo(16));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_DataTooSmall_ThrowsInvalidDataException() {
    var data = new byte[Fl32File.HeaderSize];
    BinaryPrimitives.WriteUInt32LittleEndian(data, Fl32File.Magic);
    BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(4), 100);
    BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(8), 100);
    BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(12), 3);
    Assert.Throws<InvalidDataException>(() => Fl32Reader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ViaStream_Equivalent() {
    var data = _BuildFl32(2, 2, 1);
    var fromBytes = Fl32Reader.FromBytes(data);

    using var ms = new MemoryStream(data);
    var fromStream = Fl32Reader.FromStream(ms);

    Assert.Multiple(() => {
      Assert.That(fromStream.Width, Is.EqualTo(fromBytes.Width));
      Assert.That(fromStream.Height, Is.EqualTo(fromBytes.Height));
      Assert.That(fromStream.Channels, Is.EqualTo(fromBytes.Channels));
    });
  }

  private static byte[] _BuildFl32(int width, int height, int channels) {
    var totalFloats = width * height * channels;
    var data = new byte[Fl32File.HeaderSize + totalFloats * 4];
    BinaryPrimitives.WriteUInt32LittleEndian(data, Fl32File.Magic);
    BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(4), height);
    BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(8), width);
    BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(12), channels);
    for (var i = 0; i < totalFloats; ++i)
      BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan(Fl32File.HeaderSize + i * 4), i * 0.1f);
    return data;
  }
}
