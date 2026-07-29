using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.Spectrum512Ext;

namespace FileFormat.Spectrum512Ext.Tests;

[TestFixture]
public sealed class Spectrum512ExtReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => Spectrum512ExtReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => Spectrum512ExtReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".spx"));
    Assert.Throws<FileNotFoundException>(() => Spectrum512ExtReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => Spectrum512ExtReader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_WrongSize_ThrowsInvalidDataException() {
    var tooSmall = new byte[100];
    Assert.Throws<InvalidDataException>(() => Spectrum512ExtReader.FromBytes(tooSmall));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidSpx_ParsesCorrectly() {
    var data = _BuildSpx();
    var result = Spectrum512ExtReader.FromBytes(data);

    Assert.Multiple(() => {
      Assert.That(result.Width, Is.EqualTo(320));
      Assert.That(result.Height, Is.EqualTo(199));
      Assert.That(result.PixelData, Has.Length.EqualTo(32000));
      Assert.That(result.Palettes, Has.Length.EqualTo(199));
      Assert.That(result.Palettes[0], Has.Length.EqualTo(48));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromStream_ValidSpx_ParsesCorrectly() {
    var data = _BuildSpx();
    using var stream = new MemoryStream(data);
    var result = Spectrum512ExtReader.FromStream(stream);

    Assert.Multiple(() => {
      Assert.That(result.Width, Is.EqualTo(320));
      Assert.That(result.Height, Is.EqualTo(199));
      Assert.That(result.Palettes, Has.Length.EqualTo(199));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_PixelDataPreserved() {
    var data = _BuildSpx();
    data[PixelStart] = 0xAA;
    data[PixelStart + 1] = 0xBB;
    data[PixelStart + Spectrum512ExtFile.PixelDataSize - Spectrum512ExtFile.BitmapLeadIn - 1] = 0xCC;

    var result = Spectrum512ExtReader.FromBytes(data);

    Assert.Multiple(() => {
      Assert.That(result.PixelData[0], Is.EqualTo(0xAA));
      Assert.That(result.PixelData[1], Is.EqualTo(0xBB));
      Assert.That(result.PixelData[Spectrum512ExtFile.PixelDataSize - Spectrum512ExtFile.BitmapLeadIn - 1], Is.EqualTo(0xCC));
    });
  }

  /// <summary>Builds a stored (unpacked) SPX file: header, bitmap block, then the palette block.</summary>
  private static byte[] _BuildSpx() {
    var data = new byte[Spectrum512ExtFile.FileSize];
    Spectrum512ExtFile.Signature.CopyTo(data);
    BinaryPrimitives.WriteInt32BigEndian(
      data.AsSpan(Spectrum512ExtFile.LengthFieldsOffset), Spectrum512ExtFile.PixelDataSize);
    BinaryPrimitives.WriteInt32BigEndian(
      data.AsSpan(Spectrum512ExtFile.LengthFieldsOffset + 4), Spectrum512ExtFile.PaletteDataSize);

    var picture = PixelStart;
    for (var i = 0; i < Spectrum512ExtFile.PixelDataSize - Spectrum512ExtFile.BitmapLeadIn; ++i)
      data[picture + i] = (byte)(i & 0xFF);

    for (var i = 0; i < Spectrum512ExtFile.PaletteDataSize; i += 2) {
      data[Spectrum512ExtFile.PaletteOffset + i] = (byte)((i / 2) >> 8 & 0x0F);
      data[Spectrum512ExtFile.PaletteOffset + i + 1] = (byte)((i / 2) & 0xFF);
    }

    return data;
  }

  /// <summary>Offset of the first byte of the picture, past the header and the lead-in scanline.</summary>
  private const int PixelStart = Spectrum512ExtFile.BitmapOffset + Spectrum512ExtFile.BitmapLeadIn;
}
