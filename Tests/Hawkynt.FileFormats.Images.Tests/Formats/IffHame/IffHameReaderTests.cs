using System;
using System.IO;
using FileFormat.Core;
using FileFormat.IffHame;
using FileFormat.Ilbm;

namespace FileFormat.IffHame.Tests;

[TestFixture]
public sealed class IffHameReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => IffHameReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => IffHameReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".hame"));
    Assert.Throws<FileNotFoundException>(() => IffHameReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => IffHameReader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    Assert.Throws<InvalidDataException>(() => IffHameReader.FromBytes(new byte[1]));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TwelveBytesOfNoise_ThrowsInvalidDataException() {
    Assert.Throws<InvalidDataException>(() => IffHameReader.FromBytes(new byte[12]));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_OrdinaryFourPlaneIlbmWithoutCookie_ThrowsInvalidDataException() {
    var carrier = new IlbmFile {
      Width = 640,
      Height = 2,
      NumPlanes = 4,
      Compression = IlbmCompression.None,
      Masking = IlbmMasking.None,
      XAspect = 5,
      YAspect = 11,
      PageWidth = 640,
      PageHeight = 2,
      PixelData = new byte[640 * 2],
      Palette = IffHameCodec.CreateCarrierPalette(),
      ViewportMode = 0x8000,
    };

    Assert.Throws<InvalidDataException>(() => IffHameReader.FromBytes(IlbmWriter.ToBytes(carrier)));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Valid() {
    var bytes = IffHameWriter.ToBytes(IffHameFile.FromRawImage(_Picture()));

    using var stream = new MemoryStream(bytes);
    var result = IffHameReader.FromStream(stream);

    Assert.Multiple(() => {
      Assert.That(result.Width, Is.EqualTo(320));
      Assert.That(result.Height, Is.EqualTo(4));
      Assert.That(result.PaletteCount, Is.EqualTo(64));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_CopiesRawData_NotReference() {
    var bytes = IffHameWriter.ToBytes(IffHameFile.FromRawImage(_Picture()));
    var expectedFirst = bytes[0];

    var result = IffHameReader.FromBytes(bytes);
    bytes[0] ^= 0xFF;

    Assert.That(result.RawData, Is.Not.Null);
    Assert.That(result.RawData![0], Is.EqualTo(expectedFirst));
  }

  private static RawImage _Picture() {
    var pixels = new byte[320 * 4 * 3];
    for (var i = 0; i < 320 * 4; ++i) {
      var at = i * 3;
      pixels[at] = (byte)(i % 2 == 0 ? 255 : 0);
      pixels[at + 1] = (byte)(i % 3 == 0 ? 255 : 0);
      pixels[at + 2] = (byte)(i % 5 == 0 ? 255 : 0);
    }

    return new() {
      Width = 320,
      Height = 4,
      Format = PixelFormat.Rgb24,
      PixelData = pixels,
    };
  }
}
