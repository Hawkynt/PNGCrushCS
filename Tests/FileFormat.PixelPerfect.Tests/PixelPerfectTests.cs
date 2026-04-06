using System;
using System.IO;
using FileFormat.PixelPerfect;
using FileFormat.Core;

namespace FileFormat.PixelPerfect.Tests;

[TestFixture]
public sealed class PixelPerfectReaderTests {

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => PixelPerfectReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".pp"));
    Assert.Throws<FileNotFoundException>(() => PixelPerfectReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => PixelPerfectReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    Assert.Throws<InvalidDataException>(() => PixelPerfectReader.FromBytes(new byte[100]));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ExactlyMinSize_DoesNotThrow() {
    var data = new byte[PixelPerfectFile.LoadAddressSize + PixelPerfectFile.MinBitmapSize];
    data[0] = 0x00;
    data[1] = 0x60;
    Assert.DoesNotThrow(() => PixelPerfectReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidData_ParsesDimensions() {
    var data = TestHelpers._BuildValidPixelPerfectData(0x6000);
    var result = PixelPerfectReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(160));
    Assert.That(result.Height, Is.EqualTo(200));
    Assert.That(result.LoadAddress, Is.EqualTo(0x6000));
    Assert.That(result.RawData.Length, Is.EqualTo(PixelPerfectFile.StandardPayloadSize));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_LoadAddress_LittleEndian() {
    var data = TestHelpers._BuildValidPixelPerfectData(0x4000);
    var result = PixelPerfectReader.FromBytes(data);

    Assert.That(result.LoadAddress, Is.EqualTo(0x4000));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_RawData_ExcludesLoadAddress() {
    var data = TestHelpers._BuildValidPixelPerfectData(0x6000);
    var result = PixelPerfectReader.FromBytes(data);

    Assert.That(result.RawData.Length, Is.EqualTo(data.Length - PixelPerfectFile.LoadAddressSize));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => PixelPerfectReader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_ValidData_ParsesCorrectly() {
    var data = TestHelpers._BuildValidPixelPerfectData(0x6000);
    using var ms = new MemoryStream(data);
    var result = PixelPerfectReader.FromStream(ms);

    Assert.That(result.LoadAddress, Is.EqualTo(0x6000));
    Assert.That(result.RawData.Length, Is.EqualTo(PixelPerfectFile.StandardPayloadSize));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_PixelDataCopied_NotShared() {
    var data = TestHelpers._BuildValidPixelPerfectData(0x6000);
    var result = PixelPerfectReader.FromBytes(data);

    data[PixelPerfectFile.LoadAddressSize] = 0xFF;
    Assert.That(result.RawData[0], Is.Not.EqualTo(0xFF));
  }
}

[TestFixture]
public sealed class PixelPerfectRoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllFieldsPreserved() {
    var rawData = new byte[PixelPerfectFile.StandardPayloadSize];
    for (var i = 0; i < rawData.Length; ++i)
      rawData[i] = (byte)(i * 11 % 256);

    var original = new PixelPerfectFile { LoadAddress = 0x6000, RawData = rawData };

    var bytes = PixelPerfectWriter.ToBytes(original);
    var restored = PixelPerfectReader.FromBytes(bytes);

    Assert.That(restored.LoadAddress, Is.EqualTo(original.LoadAddress));
    Assert.That(restored.RawData, Is.EqualTo(original.RawData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaFile() {
    var rawData = new byte[PixelPerfectFile.StandardPayloadSize];
    for (var i = 0; i < rawData.Length; ++i)
      rawData[i] = (byte)(i % 256);

    var original = new PixelPerfectFile { LoadAddress = 0x6000, RawData = rawData };

    var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".pp");
    try {
      File.WriteAllBytes(tempPath, PixelPerfectWriter.ToBytes(original));
      var restored = PixelPerfectReader.FromFile(new FileInfo(tempPath));

      Assert.That(restored.LoadAddress, Is.EqualTo(original.LoadAddress));
      Assert.That(restored.RawData, Is.EqualTo(original.RawData));
    } finally {
      if (File.Exists(tempPath))
        File.Delete(tempPath);
    }
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_CustomLoadAddress() {
    var rawData = new byte[PixelPerfectFile.StandardPayloadSize];
    var original = new PixelPerfectFile { LoadAddress = 0x4000, RawData = rawData };

    var bytes = PixelPerfectWriter.ToBytes(original);
    var restored = PixelPerfectReader.FromBytes(bytes);

    Assert.That(restored.LoadAddress, Is.EqualTo(0x4000));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllBytesMaxValue() {
    var rawData = new byte[PixelPerfectFile.StandardPayloadSize];
    Array.Fill(rawData, (byte)0xFF);

    var original = new PixelPerfectFile { LoadAddress = 0xFFFF, RawData = rawData };

    var bytes = PixelPerfectWriter.ToBytes(original);
    var restored = PixelPerfectReader.FromBytes(bytes);

    Assert.That(restored.LoadAddress, Is.EqualTo(original.LoadAddress));
    Assert.That(restored.RawData, Is.EqualTo(original.RawData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaRawImage_ProducesValidOutput() {
    var rawData = new byte[PixelPerfectFile.StandardPayloadSize];
    for (var i = 0; i < rawData.Length; ++i)
      rawData[i] = (byte)(i % 256);

    var file = new PixelPerfectFile { LoadAddress = 0x6000, RawData = rawData };
    var image = PixelPerfectFile.ToRawImage(file);

    Assert.That(image.Width, Is.EqualTo(PixelPerfectFile.FixedWidth));
    Assert.That(image.Height, Is.EqualTo(PixelPerfectFile.FixedHeight));
    Assert.That(image.Format, Is.EqualTo(PixelFormat.Rgb24));
    Assert.That(image.PixelData.Length, Is.EqualTo(160 * 200 * 3));
  }
}

file static class TestHelpers {
  internal static byte[] _BuildValidPixelPerfectData(ushort loadAddress) {
    var rawData = new byte[PixelPerfectFile.StandardPayloadSize];
    for (var i = 0; i < rawData.Length; ++i)
      rawData[i] = (byte)(i % 256);

    var data = new byte[PixelPerfectFile.LoadAddressSize + rawData.Length];
    data[0] = (byte)(loadAddress & 0xFF);
    data[1] = (byte)(loadAddress >> 8);
    Array.Copy(rawData, 0, data, PixelPerfectFile.LoadAddressSize, rawData.Length);
    return data;
  }
}
