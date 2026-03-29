using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.SinbadSlideshow;

namespace FileFormat.SinbadSlideshow.Tests;

[TestFixture]
public sealed class SinbadSlideshowReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => SinbadSlideshowReader.FromBytes(null!));

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => SinbadSlideshowReader.FromFile(null!));

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".ssb"));
    Assert.Throws<FileNotFoundException>(() => SinbadSlideshowReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => SinbadSlideshowReader.FromStream(null!));

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => SinbadSlideshowReader.FromBytes(new byte[100]));

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidSize_Parses() {
    var data = _BuildMinimalSinbad();
    var result = SinbadSlideshowReader.FromBytes(data);

    Assert.Multiple(() => {
      Assert.That(result.Width, Is.EqualTo(320));
      Assert.That(result.Height, Is.EqualTo(200));
      Assert.That(result.Palette.Length, Is.EqualTo(16));
      Assert.That(result.PixelData.Length, Is.EqualTo(32000));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ParsesPalette() {
    var data = _BuildMinimalSinbad();
    BinaryPrimitives.WriteInt16BigEndian(data.AsSpan(0), 0x0777);
    BinaryPrimitives.WriteInt16BigEndian(data.AsSpan(2), 0x0700);
    BinaryPrimitives.WriteInt16BigEndian(data.AsSpan(30), 0x0007);

    var result = SinbadSlideshowReader.FromBytes(data);

    Assert.Multiple(() => {
      Assert.That(result.Palette[0], Is.EqualTo((short)0x0777));
      Assert.That(result.Palette[1], Is.EqualTo((short)0x0700));
      Assert.That(result.Palette[15], Is.EqualTo((short)0x0007));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ParsesPixelData() {
    var data = _BuildMinimalSinbad();
    data[32] = 0xAA;
    data[33] = 0xBB;
    data[32031] = 0xCC;

    var result = SinbadSlideshowReader.FromBytes(data);

    Assert.Multiple(() => {
      Assert.That(result.PixelData[0], Is.EqualTo(0xAA));
      Assert.That(result.PixelData[1], Is.EqualTo(0xBB));
      Assert.That(result.PixelData[31999], Is.EqualTo(0xCC));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromStream_ValidData_Parses() {
    var data = _BuildMinimalSinbad();
    using var ms = new MemoryStream(data);
    var result = SinbadSlideshowReader.FromStream(ms);

    Assert.Multiple(() => {
      Assert.That(result.Width, Is.EqualTo(320));
      Assert.That(result.Height, Is.EqualTo(200));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_CopiesPixelData() {
    var data = _BuildMinimalSinbad();
    data[32] = 0x55;
    var result = SinbadSlideshowReader.FromBytes(data);
    data[32] = 0x00;
    Assert.That(result.PixelData[0], Is.EqualTo(0x55));
  }

  private static byte[] _BuildMinimalSinbad() {
    var data = new byte[32032];
    for (var i = 0; i < 32000; ++i)
      data[32 + i] = (byte)(i * 7 % 256);
    return data;
  }
}
