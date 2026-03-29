using System;
using System.IO;
using FileFormat.Core;
using FileFormat.PrintShop;

namespace FileFormat.PrintShop.Tests;

[TestFixture]
public sealed class PrintShopReaderTests {

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => PrintShopReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".psa"));
    Assert.Throws<FileNotFoundException>(() => PrintShopReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => PrintShopReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_WrongSize_ThrowsInvalidDataException() {
    Assert.Throws<InvalidDataException>(() => PrintShopReader.FromBytes(new byte[100]));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_Psa_Parses() {
    var data = new byte[572];
    data[0] = 0xAB;
    data[571] = 0xCD;

    var result = PrintShopReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(88));
    Assert.That(result.Height, Is.EqualTo(52));
    Assert.That(result.IsFormatB, Is.False);
    Assert.That(result.PixelData[0], Is.EqualTo(0xAB));
    Assert.That(result.PixelData[571], Is.EqualTo(0xCD));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_Psb_Parses() {
    var data = new byte[576];
    data[4] = 0xDE;

    var result = PrintShopReader.FromBytes(data);

    Assert.That(result.IsFormatB, Is.True);
    Assert.That(result.PixelData[0], Is.EqualTo(0xDE));
  }
}

[TestFixture]
public sealed class PrintShopWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => PrintShopWriter.ToBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ProducesCorrectSize() {
    var file = new PrintShopFile { PixelData = new byte[572] };
    var bytes = PrintShopWriter.ToBytes(file);
    Assert.That(bytes.Length, Is.EqualTo(572));
  }
}

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_Psa_PreservesData() {
    var pixelData = new byte[572];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 11 % 256);

    var original = new PrintShopFile { PixelData = pixelData };

    var bytes = PrintShopWriter.ToBytes(original);
    var restored = PrintShopReader.FromBytes(bytes);

    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaRawImage() {
    var pixelData = new byte[572];
    pixelData[0] = 0xFF;
    var original = new PrintShopFile { PixelData = pixelData };

    var raw = PrintShopFile.ToRawImage(original);
    var restored = PrintShopFile.FromRawImage(raw);

    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }
}

[TestFixture]
public sealed class DataTypeTests {

  [Test]
  [Category("Unit")]
  public void PrintShopFile_DefaultWidth_Is88() {
    Assert.That(new PrintShopFile().Width, Is.EqualTo(88));
  }

  [Test]
  [Category("Unit")]
  public void PrintShopFile_DefaultHeight_Is52() {
    Assert.That(new PrintShopFile().Height, Is.EqualTo(52));
  }

  [Test]
  [Category("Unit")]
  public void PrintShopFile_ToRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => PrintShopFile.ToRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void PrintShopFile_FromRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => PrintShopFile.FromRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void PrintShopFile_ToRawImage_ReturnsIndexed1() {
    var file = new PrintShopFile { PixelData = new byte[572] };
    var raw = PrintShopFile.ToRawImage(file);

    Assert.That(raw.Format, Is.EqualTo(PixelFormat.Indexed1));
    Assert.That(raw.PaletteCount, Is.EqualTo(2));
  }
}
