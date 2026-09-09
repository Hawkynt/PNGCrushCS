using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.Core;
using FileFormat.IffMultiPalette;
using FileFormat.Ilbm;

namespace FileFormat.IffMultiPalette.Tests;

[TestFixture]
public sealed class IffMultiPaletteReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => IffMultiPaletteReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => IffMultiPaletteReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".mpl"));
    Assert.Throws<FileNotFoundException>(() => IffMultiPaletteReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => IffMultiPaletteReader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    var tooSmall = new byte[IffMultiPaletteFile.MinFileSize - 1];
    Assert.Throws<InvalidDataException>(() => IffMultiPaletteReader.FromBytes(tooSmall));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_FormMpal_IsRefusedBecauseMultipaletteIsAnIlbmProperty() {
    var data = new byte[12];
    "FORM"u8.CopyTo(data);
    BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(4), 4);
    "MPAL"u8.CopyTo(data.AsSpan(8));

    var error = Assert.Throws<InvalidDataException>(() => IffMultiPaletteReader.FromBytes(data));
    Assert.That(error!.Message, Does.Contain("FORM ILBM"));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_OrdinaryIlbmWithoutPchg_IsRefused() {
    var data = IlbmWriter.ToBytes(new IlbmFile {
      Width = 8,
      Height = 2,
      NumPlanes = 1,
      Compression = IlbmCompression.None,
      Masking = IlbmMasking.None,
      TransparentColor = 0,
      XAspect = 1,
      YAspect = 1,
      PageWidth = 8,
      PageHeight = 2,
      PixelData = new byte[16],
      Palette = [0, 0, 0, 255, 255, 255],
    });

    var error = Assert.Throws<InvalidDataException>(() => IffMultiPaletteReader.FromBytes(data));
    Assert.That(error!.Message, Does.Contain("no PCHG"));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_CompressedPchg_IsRefusedByName() {
    var data = _ValidFile();
    var pchg = _FindChunk(data, "PCHG");
    BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(pchg + 8), 1);

    var error = Assert.Throws<NotSupportedException>(() => IffMultiPaletteReader.FromBytes(data));
    Assert.That(error!.Message, Does.Contain("Compressed PCHG"));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ThirtyTwoBitPchg_IsRefusedByName() {
    var data = _ValidFile();
    var pchg = _FindChunk(data, "PCHG");
    BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(pchg + 10), 2);

    var error = Assert.Throws<NotSupportedException>(() => IffMultiPaletteReader.FromBytes(data));
    Assert.That(error!.Message, Does.Contain("32-bit PCHG"));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidWriterOutput_ParsesSemanticImage() {
    var data = _ValidFile(13, 5);

    var result = IffMultiPaletteReader.FromBytes(data);

    Assert.Multiple(() => {
      Assert.That((result.Width, result.Height), Is.EqualTo((13, 5)));
      Assert.That(result.NumPlanes, Is.EqualTo(4));
      Assert.That(result.PixelData, Has.Length.EqualTo(13 * 5));
      Assert.That(result.Palette, Has.Length.EqualTo(IffMultiPaletteFile.PaletteBytes));
      Assert.That(result.ScanlinePalettes, Has.Length.EqualTo(5 * IffMultiPaletteFile.PaletteBytes));
      Assert.That(result.RawData, Is.EqualTo(data));
      Assert.That(result.RawData, Is.Not.SameAs(data));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromStream_ValidWriterOutput_ParsesCorrectly() {
    var data = _ValidFile(16, 3);
    using var stream = new MemoryStream(data);

    var result = IffMultiPaletteReader.FromStream(stream);

    Assert.That((result.Width, result.Height), Is.EqualTo((16, 3)));
  }

  private static byte[] _ValidFile(int width = 8, int height = 4) {
    var pixels = new byte[width * height * 3];
    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x) {
      var at = (y * width + x) * 3;
      pixels[at] = (byte)(((x + y) & 1) == 0 ? 0xFF : 0x00);
      pixels[at + 1] = (byte)((x % 3) * 0x55);
      pixels[at + 2] = (byte)((y % 3) * 0x55);
    }

    var file = IffMultiPaletteFile.FromRawImage(new() {
      Width = width,
      Height = height,
      Format = PixelFormat.Rgb24,
      PixelData = pixels,
    });
    return IffMultiPaletteWriter.ToBytes(file);
  }

  private static int _FindChunk(byte[] data, string id) {
    var expected = System.Text.Encoding.ASCII.GetBytes(id);
    var offset = 12;
    while (offset + 8 <= data.Length) {
      var length = checked((int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(offset + 4)));
      if (data.AsSpan(offset, 4).SequenceEqual(expected))
        return offset;
      offset = checked(offset + 8 + length + (length & 1));
    }

    Assert.Fail($"Missing {id} chunk.");
    return -1;
  }
}
