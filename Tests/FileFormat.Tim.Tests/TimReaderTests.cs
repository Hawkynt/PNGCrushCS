using System;
using System.IO;
using FileFormat.Tim;

namespace FileFormat.Tim.Tests;

[TestFixture]
public sealed class TimReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => TimReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => TimReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".tim"));
    Assert.Throws<FileNotFoundException>(() => TimReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    var tooSmall = new byte[4];
    Assert.Throws<InvalidDataException>(() => TimReader.FromBytes(tooSmall));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_InvalidMagic_ThrowsInvalidDataException() {
    var bad = new byte[20];
    bad[0] = 0xFF;
    Assert.Throws<InvalidDataException>(() => TimReader.FromBytes(bad));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_Valid16bpp_ParsesCorrectly() {
    var data = _BuildMinimalTim(TimBpp.Bpp16, hasClut: false, width: 4, height: 2);
    var result = TimReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(4));
    Assert.That(result.Height, Is.EqualTo(2));
    Assert.That(result.Bpp, Is.EqualTo(TimBpp.Bpp16));
    Assert.That(result.HasClut, Is.False);
    Assert.That(result.ClutData, Is.Null);
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_Valid8bppWithClut_ParsesCorrectly() {
    var data = _BuildMinimalTim(TimBpp.Bpp8, hasClut: true, width: 4, height: 2);
    var result = TimReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(4));
    Assert.That(result.Height, Is.EqualTo(2));
    Assert.That(result.Bpp, Is.EqualTo(TimBpp.Bpp8));
    Assert.That(result.HasClut, Is.True);
    Assert.That(result.ClutData, Is.Not.Null);
    Assert.That(result.ClutData!.Length, Is.GreaterThan(0));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_Valid4bppWithClut_ParsesCorrectly() {
    var data = _BuildMinimalTim(TimBpp.Bpp4, hasClut: true, width: 8, height: 2);
    var result = TimReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(8));
    Assert.That(result.Height, Is.EqualTo(2));
    Assert.That(result.Bpp, Is.EqualTo(TimBpp.Bpp4));
    Assert.That(result.HasClut, Is.True);
    Assert.That(result.ClutData, Is.Not.Null);
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_Valid24bpp_ParsesCorrectly() {
    var data = _BuildMinimalTim(TimBpp.Bpp24, hasClut: false, width: 6, height: 2);
    var result = TimReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(6));
    Assert.That(result.Height, Is.EqualTo(2));
    Assert.That(result.Bpp, Is.EqualTo(TimBpp.Bpp24));
    Assert.That(result.HasClut, Is.False);
  }

  [Test]
  [Category("Unit")]
  public void FromStream_ValidData_ParsesCorrectly() {
    var data = _BuildMinimalTim(TimBpp.Bpp16, hasClut: false, width: 4, height: 2);
    using var stream = new MemoryStream(data);
    var result = TimReader.FromStream(stream);

    Assert.That(result.Width, Is.EqualTo(4));
    Assert.That(result.Height, Is.EqualTo(2));
    Assert.That(result.Bpp, Is.EqualTo(TimBpp.Bpp16));
  }

  private static byte[] _BuildMinimalTim(TimBpp bpp, bool hasClut, int width, int height) {
    using var ms = new MemoryStream();
    using var bw = new BinaryWriter(ms);

    uint flags = (uint)bpp;
    if (hasClut)
      flags |= 0x08;

    bw.Write((uint)0x10);
    bw.Write(flags);

    if (hasClut) {
      var clutColors = bpp == TimBpp.Bpp4 ? 16 : 256;
      var clutDataSize = clutColors * 2;
      var clutBlockSize = (uint)(12 + clutDataSize);
      bw.Write(clutBlockSize);
      bw.Write((ushort)0); // X
      bw.Write((ushort)0); // Y
      bw.Write((ushort)clutColors); // Width
      bw.Write((ushort)1); // Height
      bw.Write(new byte[clutDataSize]);
    }

    var vramWidth = bpp switch {
      TimBpp.Bpp4 => width / 4,
      TimBpp.Bpp8 => width / 2,
      TimBpp.Bpp16 => width,
      TimBpp.Bpp24 => width * 3 / 2,
      _ => width
    };
    var pixelDataSize = vramWidth * 2 * height;
    var imageBlockSize = (uint)(12 + pixelDataSize);

    bw.Write(imageBlockSize);
    bw.Write((ushort)0); // X
    bw.Write((ushort)0); // Y
    bw.Write((ushort)vramWidth);
    bw.Write((ushort)height);
    bw.Write(new byte[pixelDataSize]);

    return ms.ToArray();
  }
}
