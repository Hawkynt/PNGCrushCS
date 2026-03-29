using System;
using System.IO;
using FileFormat.ZxNext;
using FileFormat.Core;

namespace FileFormat.ZxNext.Tests;

[TestFixture]
public class ZxNextReaderTests {

  [Test]
  public void FromFile_NullFile_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => ZxNextReader.FromFile(null!));

  [Test]
  public void FromFile_MissingFile_ThrowsFileNotFoundException()
    => Assert.Throws<FileNotFoundException>(() => ZxNextReader.FromFile(new FileInfo("nonexistent.nxt")));

  [Test]
  public void FromBytes_NullData_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => ZxNextReader.FromBytes(null!));

  [Test]
  public void FromBytes_TooSmall_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => ZxNextReader.FromBytes(new byte[100]));

  [Test]
  public void FromBytes_ExactSize_Succeeds() {
    var data = new byte[49152];
    var result = ZxNextReader.FromBytes(data);
    Assert.That(result.Width, Is.EqualTo(256));
    Assert.That(result.Height, Is.EqualTo(192));
  }

  [Test]
  public void FromStream_NullStream_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => ZxNextReader.FromStream(null!));

  [Test]
  public void FromBytes_WrongSize_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => ZxNextReader.FromBytes(new byte[50000]));

  [Test]
  public void FromBytes_PixelData_HasCorrectLength() {
    var data = new byte[49152];
    var result = ZxNextReader.FromBytes(data);
    Assert.That(result.PixelData.Length, Is.EqualTo(49152));
  }

  [Test]
  public void FromBytes_PixelData_IsCopy() {
    var data = new byte[49152];
    data[0] = 0xAA;
    var result = ZxNextReader.FromBytes(data);
    Assert.That(result.PixelData[0], Is.EqualTo(0xAA));
    data[0] = 0x00;
    Assert.That(result.PixelData[0], Is.EqualTo(0xAA));
  }
}

[TestFixture]
public class ZxNextWriterTests {

  [Test]
  public void ToBytes_NullFile_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => ZxNextWriter.ToBytes(null!));

  [Test]
  public void ToBytes_OutputSize_Is49152() {
    var file = new ZxNextFile { PixelData = new byte[49152] };
    var bytes = ZxNextWriter.ToBytes(file);
    Assert.That(bytes.Length, Is.EqualTo(49152));
  }

  [Test]
  public void ToBytes_DataPreserved() {
    var pixelData = new byte[49152];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i & 0xFF);
    var file = new ZxNextFile { PixelData = pixelData };
    var bytes = ZxNextWriter.ToBytes(file);
    Assert.That(bytes, Is.EqualTo(pixelData));
  }
}

[TestFixture]
public class RoundTripTests {

  [Test]
  public void RoundTrip_AllBytes_Preserved() {
    var original = new byte[49152];
    for (var i = 0; i < original.Length; ++i)
      original[i] = (byte)(i * 7 & 0xFF);
    var file = ZxNextReader.FromBytes(original);
    var written = ZxNextWriter.ToBytes(file);
    Assert.That(written, Is.EqualTo(original));
  }

  [Test]
  public void RoundTrip_ViaFile() {
    var original = new byte[49152];
    for (var i = 0; i < original.Length; ++i)
      original[i] = (byte)(i & 0xFF);
    var tmp = Path.GetTempFileName();
    try {
      File.WriteAllBytes(tmp, original);
      var file = ZxNextReader.FromFile(new FileInfo(tmp));
      var written = ZxNextWriter.ToBytes(file);
      Assert.That(written, Is.EqualTo(original));
    } finally {
      File.Delete(tmp);
    }
  }

  [Test]
  public void RoundTrip_ViaRawImage() {
    var pixelData = new byte[256 * 192];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i & 0xFF);
    var original = new ZxNextFile { PixelData = pixelData };
    var raw = ZxNextFile.ToRawImage(original);
    var reconstructed = ZxNextFile.FromRawImage(raw);
    Assert.That(reconstructed.PixelData, Is.EqualTo(original.PixelData));
  }
}

[TestFixture]
public class DataTypeTests {

  [Test]
  public void Width_Is256() {
    var file = new ZxNextFile();
    Assert.That(file.Width, Is.EqualTo(256));
  }

  [Test]
  public void Height_Is192() {
    var file = new ZxNextFile();
    Assert.That(file.Height, Is.EqualTo(192));
  }

  [Test]
  public void PixelData_DefaultIsEmpty() {
    var file = new ZxNextFile();
    Assert.That(file.PixelData, Is.Empty);
  }

  [Test]
  public void ToRawImage_NullFile_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => ZxNextFile.ToRawImage(null!));

  [Test]
  public void FromRawImage_NullImage_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => ZxNextFile.FromRawImage(null!));

  [Test]
  public void FromRawImage_WrongFormat_ThrowsNotSupportedException() {
    var raw = new RawImage { Width = 256, Height = 192, Format = PixelFormat.Rgb24, PixelData = new byte[256 * 192 * 3] };
    Assert.Throws<NotSupportedException>(() => ZxNextFile.FromRawImage(raw));
  }

  [Test]
  public void FromRawImage_WrongDimensions_ThrowsNotSupportedException() {
    var raw = new RawImage { Width = 128, Height = 128, Format = PixelFormat.Indexed8, PixelData = new byte[128 * 128] };
    Assert.Throws<NotSupportedException>(() => ZxNextFile.FromRawImage(raw));
  }

  [Test]
  public void ToRawImage_ReturnsIndexed8() {
    var file = new ZxNextFile { PixelData = new byte[49152] };
    var raw = ZxNextFile.ToRawImage(file);
    Assert.That(raw.Format, Is.EqualTo(PixelFormat.Indexed8));
    Assert.That(raw.Width, Is.EqualTo(256));
    Assert.That(raw.Height, Is.EqualTo(192));
  }

  [Test]
  public void ToRawImage_Has256PaletteEntries() {
    var file = new ZxNextFile { PixelData = new byte[49152] };
    var raw = ZxNextFile.ToRawImage(file);
    Assert.That(raw.PaletteCount, Is.EqualTo(256));
    Assert.That(raw.Palette.Length, Is.EqualTo(768));
  }
}
