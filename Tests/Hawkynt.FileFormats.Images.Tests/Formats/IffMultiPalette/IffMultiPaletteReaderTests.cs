using System;
using System.IO;
using FileFormat.Core;
using FileFormat.Ilbm;
using FileFormat.IffMultiPalette;

namespace FileFormat.IffMultiPalette.Tests;

[TestFixture]
public sealed class IffMultiPaletteReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => IffMultiPaletteReader.FromBytes(null!));

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => IffMultiPaletteReader.FromFile(null!));

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".mpl"));
    Assert.Throws<FileNotFoundException>(() => IffMultiPaletteReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => IffMultiPaletteReader.FromStream(null!));

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => IffMultiPaletteReader.FromBytes(new byte[11]));

  [Test]
  [Category("Unit")]
  public void FromBytes_FormerInventedFormMpalIdentity_IsRefused() {
    var data = new byte[32];
    "FORM"u8.CopyTo(data);
    "MPAL"u8.CopyTo(data.AsSpan(8));

    var exception = Assert.Throws<InvalidDataException>(() => IffMultiPaletteReader.FromBytes(data));
    Assert.That(exception!.Message, Does.Contain("FORM ILBM"));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_PlainIlbmWithoutPchg_IsRefused() {
    var raw = new RawImage {
      Width = 2,
      Height = 2,
      Format = PixelFormat.Rgb24,
      PixelData = [0, 0, 0, 255, 255, 255, 255, 0, 0, 0, 255, 0],
    };
    var bytes = IlbmWriter.ToBytes(IlbmFile.FromRawImage(raw));

    var exception = Assert.Throws<InvalidDataException>(() => IffMultiPaletteReader.FromBytes(bytes));
    Assert.That(exception!.Message, Does.Contain("PCHG"));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_WriterOutput_ReadsPixelsAndPalettes() {
    var raw = new RawImage {
      Width = 4,
      Height = 2,
      Format = PixelFormat.Rgb24,
      PixelData = [
        0, 0, 0, 17, 0, 0, 34, 0, 0, 51, 0, 0,
        0, 17, 0, 17, 17, 0, 34, 17, 0, 51, 17, 0,
      ],
    };
    var file = IffMultiPaletteFile.FromRawImage(raw);
    using var stream = new MemoryStream(IffMultiPaletteWriter.ToBytes(file));

    var result = IffMultiPaletteReader.FromStream(stream);

    Assert.Multiple(() => {
      Assert.That(result.Width, Is.EqualTo(4));
      Assert.That(result.Height, Is.EqualTo(2));
      Assert.That(result.PixelData, Has.Length.EqualTo(8));
      Assert.That(result.ScanlinePalettes, Has.Length.EqualTo(2 * IffMultiPaletteFile.PaletteByteSize));
      Assert.That(IffMultiPaletteFile.ToRawImage(result).PixelData, Is.EqualTo(raw.PixelData));
    });
  }
}
