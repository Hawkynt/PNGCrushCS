using System;
using System.IO;
using FileFormat.Tim2;

namespace FileFormat.Tim2.Tests;

[TestFixture]
public sealed class Tim2ReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => Tim2Reader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => Tim2Reader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".tm2"));
    Assert.Throws<FileNotFoundException>(() => Tim2Reader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    var tooSmall = new byte[8];
    Assert.Throws<InvalidDataException>(() => Tim2Reader.FromBytes(tooSmall));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_InvalidSignature_ThrowsInvalidDataException() {
    var bad = new byte[64];
    bad[0] = (byte)'X';
    bad[1] = (byte)'Y';
    bad[2] = (byte)'Z';
    bad[3] = (byte)'W';
    Assert.Throws<InvalidDataException>(() => Tim2Reader.FromBytes(bad));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidSinglePicture_ParsesCorrectly() {
    var data = _BuildMinimalTim2(1, Tim2Format.Rgb32, 4, 2);
    var result = Tim2Reader.FromBytes(data);

    Assert.That(result.Pictures, Has.Count.EqualTo(1));
    Assert.That(result.Pictures[0].Width, Is.EqualTo(4));
    Assert.That(result.Pictures[0].Height, Is.EqualTo(2));
    Assert.That(result.Pictures[0].Format, Is.EqualTo(Tim2Format.Rgb32));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidMultiPicture_ParsesCorrectly() {
    var data = _BuildMinimalTim2(3, Tim2Format.Rgb32, 2, 2);
    var result = Tim2Reader.FromBytes(data);

    Assert.That(result.Pictures, Has.Count.EqualTo(3));
    for (var i = 0; i < 3; ++i) {
      Assert.That(result.Pictures[i].Width, Is.EqualTo(2));
      Assert.That(result.Pictures[i].Height, Is.EqualTo(2));
    }
  }

  [Test]
  [Category("Unit")]
  public void FromStream_ValidData_ParsesCorrectly() {
    var data = _BuildMinimalTim2(1, Tim2Format.Rgb32, 4, 2);
    using var stream = new MemoryStream(data);
    var result = Tim2Reader.FromStream(stream);

    Assert.That(result.Pictures, Has.Count.EqualTo(1));
    Assert.That(result.Pictures[0].Width, Is.EqualTo(4));
  }

  private static byte[] _BuildMinimalTim2(int pictureCount, Tim2Format format, int width, int height) {
    using var ms = new MemoryStream();
    using var bw = new BinaryWriter(ms);

    bw.Write((byte)'T');
    bw.Write((byte)'I');
    bw.Write((byte)'M');
    bw.Write((byte)'2');
    bw.Write((byte)4);  // version
    bw.Write((byte)0);  // alignment
    bw.Write((ushort)pictureCount);
    bw.Write(new byte[8]); // padding

    for (var i = 0; i < pictureCount; ++i) {
      var bpp = format switch {
        Tim2Format.Rgb32 => 4,
        Tim2Format.Rgb24 => 3,
        Tim2Format.Rgb16 => 2,
        _ => 1
      };
      var imageDataSize = (uint)(width * height * bpp);
      var totalSize = (uint)Tim2PictureHeader.StructSize + imageDataSize;

      bw.Write(totalSize);
      bw.Write((uint)0); // paletteSize
      bw.Write(imageDataSize);
      bw.Write((ushort)Tim2PictureHeader.StructSize); // headerSize
      bw.Write((ushort)0); // paletteColors
      bw.Write((byte)format);
      bw.Write((byte)1);  // mipmaps
      bw.Write((byte)0);  // paletteType
      bw.Write((byte)0);  // imageType
      bw.Write((ushort)width);
      bw.Write((ushort)height);
      bw.Write((ulong)0); // GsTex0
      bw.Write((ulong)0); // GsTex1
      bw.Write((uint)0);  // GsFlags
      bw.Write((uint)0);  // GsTexClut

      bw.Write(new byte[imageDataSize]);
    }

    return ms.ToArray();
  }
}
