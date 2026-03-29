using System;
using System.IO;
using FileFormat.CompW;

namespace FileFormat.CompW.Tests;

[TestFixture]
public sealed class CompWReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => CompWReader.FromBytes(null!));

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => CompWReader.FromFile(null!));

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".wlm"));
    Assert.Throws<FileNotFoundException>(() => CompWReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => CompWReader.FromStream(null!));

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => CompWReader.FromBytes(new byte[4]));

  [Test]
  [Category("Unit")]
  public void FromBytes_InvalidMagic_ThrowsInvalidDataException() {
    var data = new byte[CompWFile.HeaderSize + 4 + CompWFile.PaletteSize];
    Assert.Throws<InvalidDataException>(() => CompWReader.FromBytes(data));
  }

  [Test]
  [Category("Integration")]
  public void FromBytes_ValidParsesCorrectly() {
    var data = _BuildValidWlm(4, 2);
    var result = CompWReader.FromBytes(data);

    Assert.Multiple(() => {
      Assert.That(result.Width, Is.EqualTo(4));
      Assert.That(result.Height, Is.EqualTo(2));
      Assert.That(result.PixelData.Length, Is.EqualTo(8));
      Assert.That(result.Palette.Length, Is.EqualTo(768));
    });
  }

  [Test]
  [Category("Integration")]
  public void FromBytes_RoundTrip_PreservesData() {
    var original = _BuildValidWlm(3, 3);
    var file = CompWReader.FromBytes(original);
    var written = CompWWriter.ToBytes(file);
    var reread = CompWReader.FromBytes(written);

    Assert.Multiple(() => {
      Assert.That(reread.Width, Is.EqualTo(file.Width));
      Assert.That(reread.Height, Is.EqualTo(file.Height));
      Assert.That(reread.PixelData, Is.EqualTo(file.PixelData));
      Assert.That(reread.Palette, Is.EqualTo(file.Palette));
    });
  }

  [Test]
  [Category("Integration")]
  public void FromStream_ValidParsesCorrectly() {
    var data = _BuildValidWlm(2, 2);
    using var ms = new MemoryStream(data);
    var result = CompWReader.FromStream(ms);

    Assert.That(result.Width, Is.EqualTo(2));
  }

  private static byte[] _BuildValidWlm(int width, int height) {
    var pixelCount = width * height;
    var data = new byte[CompWFile.HeaderSize + pixelCount + CompWFile.PaletteSize];

    data[0] = 0x43; // 'C'
    data[1] = 0x57; // 'W'
    BitConverter.TryWriteBytes(new Span<byte>(data, 2, 2), (ushort)width);
    BitConverter.TryWriteBytes(new Span<byte>(data, 4, 2), (ushort)height);
    BitConverter.TryWriteBytes(new Span<byte>(data, 6, 2), (ushort)8); // bpp

    for (var i = 0; i < pixelCount; ++i)
      data[CompWFile.HeaderSize + i] = (byte)(i % 256);

    var palOffset = CompWFile.HeaderSize + pixelCount;
    for (var i = 0; i < 256; ++i) {
      data[palOffset + i * 3] = (byte)i;
      data[palOffset + i * 3 + 1] = (byte)i;
      data[palOffset + i * 3 + 2] = (byte)i;
    }

    return data;
  }
}
