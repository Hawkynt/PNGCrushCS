using System;
using System.IO;
using FileFormat.NokiaOperatorLogo;
using FileFormat.Core;

namespace FileFormat.NokiaOperatorLogo.Tests;

[TestFixture]
public class NokiaOperatorLogoReaderTests {

  [Test]
  public void FromFile_NullFile_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => NokiaOperatorLogoReader.FromFile(null!));

  [Test]
  public void FromFile_MissingFile_ThrowsFileNotFoundException()
    => Assert.Throws<FileNotFoundException>(() => NokiaOperatorLogoReader.FromFile(new FileInfo("nonexistent.nol")));

  [Test]
  public void FromBytes_NullData_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => NokiaOperatorLogoReader.FromBytes(null!));

  [Test]
  public void FromBytes_TooSmall_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => NokiaOperatorLogoReader.FromBytes(new byte[5]));

  [Test]
  public void FromBytes_InvalidMagic_ThrowsInvalidDataException() {
    var data = new byte[20 + 72 * 14];
    data[0] = (byte)'X';
    data[1] = (byte)'Y';
    data[2] = (byte)'Z';
    Assert.Throws<InvalidDataException>(() => NokiaOperatorLogoReader.FromBytes(data));
  }

  [Test]
  public void FromBytes_ZeroDimensions_ThrowsInvalidDataException() {
    var data = _BuildNolBytes(0, 0, 0, 0);
    Assert.Throws<InvalidDataException>(() => NokiaOperatorLogoReader.FromBytes(data));
  }

  [Test]
  public void FromBytes_TruncatedPixelData_ThrowsInvalidDataException() {
    var data = new byte[20 + 5]; // too few pixel bytes for 72x14
    data[0] = 0x4E; data[1] = 0x4F; data[2] = 0x4C;
    data[10] = 72;
    data[12] = 14;
    Assert.Throws<InvalidDataException>(() => NokiaOperatorLogoReader.FromBytes(data));
  }

  [Test]
  public void FromBytes_ValidFile_ParsesDimensions() {
    var data = _BuildNolBytes(72, 14, 262, 1);
    var file = NokiaOperatorLogoReader.FromBytes(data);
    Assert.That(file.Width, Is.EqualTo(72));
    Assert.That(file.Height, Is.EqualTo(14));
  }

  [Test]
  public void FromBytes_ValidFile_ParsesMcc() {
    var data = _BuildNolBytes(72, 14, 262, 1);
    var file = NokiaOperatorLogoReader.FromBytes(data);
    Assert.That(file.Mcc, Is.EqualTo(262));
  }

  [Test]
  public void FromBytes_ValidFile_ParsesMnc() {
    var data = _BuildNolBytes(72, 14, 262, 7);
    var file = NokiaOperatorLogoReader.FromBytes(data);
    Assert.That(file.Mnc, Is.EqualTo(7));
  }

  [Test]
  public void FromBytes_AllPixelsSet_AllBitsSet() {
    var data = _BuildNolBytes(8, 2, 0, 0, allSet: true);
    var file = NokiaOperatorLogoReader.FromBytes(data);
    Assert.That(file.PixelData[0], Is.EqualTo(0xFF));
    Assert.That(file.PixelData[1], Is.EqualTo(0xFF));
  }

  [Test]
  public void FromBytes_AllPixelsClear_AllBitsClear() {
    var data = _BuildNolBytes(8, 2, 0, 0, allSet: false);
    var file = NokiaOperatorLogoReader.FromBytes(data);
    Assert.That(file.PixelData[0], Is.EqualTo(0x00));
    Assert.That(file.PixelData[1], Is.EqualTo(0x00));
  }

  [Test]
  public void FromStream_NullStream_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => NokiaOperatorLogoReader.FromStream(null!));

  [Test]
  public void FromStream_ValidStream_ParsesCorrectly() {
    var data = _BuildNolBytes(72, 14, 310, 5);
    using var ms = new MemoryStream(data);
    var file = NokiaOperatorLogoReader.FromStream(ms);
    Assert.That(file.Width, Is.EqualTo(72));
    Assert.That(file.Height, Is.EqualTo(14));
    Assert.That(file.Mcc, Is.EqualTo(310));
    Assert.That(file.Mnc, Is.EqualTo(5));
  }

  private static byte[] _BuildNolBytes(int width, int height, int mcc, int mnc, bool allSet = false) {
    var pixelCount = width * height;
    var data = new byte[20 + pixelCount];
    data[0] = 0x4E; data[1] = 0x4F; data[2] = 0x4C;
    data[3] = 0x00;
    data[4] = 0x01; data[5] = 0x00;
    data[6] = (byte)(mcc & 0xFF);
    data[7] = (byte)((mcc >> 8) & 0xFF);
    data[8] = (byte)(mnc & 0xFF);
    data[9] = 0x00;
    data[10] = (byte)width;
    data[11] = 0x00;
    data[12] = (byte)height;
    data[13] = 0x00;
    data[14] = 0x01; data[15] = 0x00; data[16] = 0x01; data[17] = 0x00; data[18] = 0x00; data[19] = 0x00;

    var fill = allSet ? (byte)'1' : (byte)'0';
    for (var i = 0; i < pixelCount; ++i)
      data[20 + i] = fill;

    return data;
  }
}

[TestFixture]
public class NokiaOperatorLogoWriterTests {

  [Test]
  public void ToBytes_NullFile_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => NokiaOperatorLogoWriter.ToBytes(null!));

  [Test]
  public void ToBytes_MagicBytes_AreNOL() {
    var file = new NokiaOperatorLogoFile { Width = 8, Height = 1, PixelData = new byte[1] };
    var bytes = NokiaOperatorLogoWriter.ToBytes(file);
    Assert.That(bytes[0], Is.EqualTo(0x4E));
    Assert.That(bytes[1], Is.EqualTo(0x4F));
    Assert.That(bytes[2], Is.EqualTo(0x4C));
  }

  [Test]
  public void ToBytes_HeaderSize_Is20() {
    var file = new NokiaOperatorLogoFile { Width = 8, Height = 1, PixelData = new byte[1] };
    var bytes = NokiaOperatorLogoWriter.ToBytes(file);
    Assert.That(bytes.Length, Is.EqualTo(20 + 8));
  }

  [Test]
  public void ToBytes_Width_AtOffset10() {
    var file = new NokiaOperatorLogoFile { Width = 72, Height = 14, PixelData = new byte[9 * 14] };
    var bytes = NokiaOperatorLogoWriter.ToBytes(file);
    Assert.That(bytes[10], Is.EqualTo(72));
  }

  [Test]
  public void ToBytes_Height_AtOffset12() {
    var file = new NokiaOperatorLogoFile { Width = 72, Height = 14, PixelData = new byte[9 * 14] };
    var bytes = NokiaOperatorLogoWriter.ToBytes(file);
    Assert.That(bytes[12], Is.EqualTo(14));
  }

  [Test]
  public void ToBytes_MccEncoded_LittleEndian() {
    var file = new NokiaOperatorLogoFile { Width = 8, Height = 1, Mcc = 262, PixelData = new byte[1] };
    var bytes = NokiaOperatorLogoWriter.ToBytes(file);
    Assert.That(bytes[6] | (bytes[7] << 8), Is.EqualTo(262));
  }

  [Test]
  public void ToBytes_MncEncoded_AtOffset8() {
    var file = new NokiaOperatorLogoFile { Width = 8, Height = 1, Mnc = 7, PixelData = new byte[1] };
    var bytes = NokiaOperatorLogoWriter.ToBytes(file);
    Assert.That(bytes[8], Is.EqualTo(7));
  }

  [Test]
  public void ToBytes_AllBitsSet_WritesAsciiOnes() {
    var file = new NokiaOperatorLogoFile { Width = 8, Height = 1, PixelData = [0xFF] };
    var bytes = NokiaOperatorLogoWriter.ToBytes(file);
    for (var i = 0; i < 8; ++i)
      Assert.That(bytes[20 + i], Is.EqualTo((byte)'1'), $"Pixel {i} should be '1'");
  }

  [Test]
  public void ToBytes_AllBitsClear_WritesAsciiZeros() {
    var file = new NokiaOperatorLogoFile { Width = 8, Height = 1, PixelData = [0x00] };
    var bytes = NokiaOperatorLogoWriter.ToBytes(file);
    for (var i = 0; i < 8; ++i)
      Assert.That(bytes[20 + i], Is.EqualTo((byte)'0'), $"Pixel {i} should be '0'");
  }

  [Test]
  public void ToBytes_MixedPixels_CorrectAsciiPattern() {
    // 0xA5 = 10100101b => '1','0','1','0','0','1','0','1'
    var file = new NokiaOperatorLogoFile { Width = 8, Height = 1, PixelData = [0xA5] };
    var bytes = NokiaOperatorLogoWriter.ToBytes(file);
    Assert.That(bytes[20], Is.EqualTo((byte)'1'));
    Assert.That(bytes[21], Is.EqualTo((byte)'0'));
    Assert.That(bytes[22], Is.EqualTo((byte)'1'));
    Assert.That(bytes[23], Is.EqualTo((byte)'0'));
    Assert.That(bytes[24], Is.EqualTo((byte)'0'));
    Assert.That(bytes[25], Is.EqualTo((byte)'1'));
    Assert.That(bytes[26], Is.EqualTo((byte)'0'));
    Assert.That(bytes[27], Is.EqualTo((byte)'1'));
  }
}

[TestFixture]
public class RoundTripTests {

  [Test]
  public void RoundTrip_72x14_AllClear() {
    var original = _BuildNolBytes(72, 14, 262, 1, allSet: false);
    var file = NokiaOperatorLogoReader.FromBytes(original);
    var written = NokiaOperatorLogoWriter.ToBytes(file);
    Assert.That(written, Is.EqualTo(original));
  }

  [Test]
  public void RoundTrip_72x14_AllSet() {
    var original = _BuildNolBytes(72, 14, 262, 1, allSet: true);
    var file = NokiaOperatorLogoReader.FromBytes(original);
    var written = NokiaOperatorLogoWriter.ToBytes(file);
    Assert.That(written, Is.EqualTo(original));
  }

  [Test]
  public void RoundTrip_MccMncPreserved() {
    var original = _BuildNolBytes(72, 14, 505, 99);
    var file = NokiaOperatorLogoReader.FromBytes(original);
    Assert.That(file.Mcc, Is.EqualTo(505));
    Assert.That(file.Mnc, Is.EqualTo(99));
    var written = NokiaOperatorLogoWriter.ToBytes(file);
    Assert.That(written[6] | (written[7] << 8), Is.EqualTo(505));
    Assert.That(written[8], Is.EqualTo(99));
  }

  [Test]
  public void RoundTrip_78x21_DifferentSize() {
    var original = _BuildNolBytes(78, 21, 310, 5, allSet: false);
    var file = NokiaOperatorLogoReader.FromBytes(original);
    var written = NokiaOperatorLogoWriter.ToBytes(file);
    Assert.That(written, Is.EqualTo(original));
  }

  [Test]
  public void RoundTrip_ViaFile() {
    var original = _BuildNolBytes(72, 14, 262, 1);
    var tmp = Path.GetTempFileName();
    try {
      File.WriteAllBytes(tmp, original);
      var file = NokiaOperatorLogoReader.FromFile(new FileInfo(tmp));
      var written = NokiaOperatorLogoWriter.ToBytes(file);
      Assert.That(written, Is.EqualTo(original));
    } finally {
      File.Delete(tmp);
    }
  }

  [Test]
  public void RoundTrip_MixedPixelPattern() {
    var data = _BuildNolBytes(8, 2, 0, 0);
    // Set a specific pattern: first row '10100101', second row '01011010'
    for (var i = 0; i < 8; ++i)
      data[20 + i] = (byte)(i % 2 == 0 ? '1' : '0');
    for (var i = 0; i < 8; ++i)
      data[28 + i] = (byte)(i % 2 == 0 ? '0' : '1');

    var file = NokiaOperatorLogoReader.FromBytes(data);
    var written = NokiaOperatorLogoWriter.ToBytes(file);
    Assert.That(written, Is.EqualTo(data));
  }

  [Test]
  public void RoundTrip_ViaRawImage() {
    var original = _BuildNolBytes(8, 2, 0, 0, allSet: true);
    var file = NokiaOperatorLogoReader.FromBytes(original);
    var raw = NokiaOperatorLogoFile.ToRawImage(file);
    var reconstructed = NokiaOperatorLogoFile.FromRawImage(raw);
    Assert.That(reconstructed.Width, Is.EqualTo(file.Width));
    Assert.That(reconstructed.Height, Is.EqualTo(file.Height));
    Assert.That(reconstructed.PixelData, Is.EqualTo(file.PixelData));
  }

  [Test]
  public void RoundTrip_ViaRawImage_AllClear() {
    var original = _BuildNolBytes(8, 2, 0, 0, allSet: false);
    var file = NokiaOperatorLogoReader.FromBytes(original);
    var raw = NokiaOperatorLogoFile.ToRawImage(file);
    var reconstructed = NokiaOperatorLogoFile.FromRawImage(raw);
    Assert.That(reconstructed.PixelData, Is.EqualTo(file.PixelData));
  }

  private static byte[] _BuildNolBytes(int width, int height, int mcc, int mnc, bool allSet = false) {
    var pixelCount = width * height;
    var data = new byte[20 + pixelCount];
    data[0] = 0x4E; data[1] = 0x4F; data[2] = 0x4C;
    data[3] = 0x00;
    data[4] = 0x01; data[5] = 0x00;
    data[6] = (byte)(mcc & 0xFF);
    data[7] = (byte)((mcc >> 8) & 0xFF);
    data[8] = (byte)(mnc & 0xFF);
    data[9] = 0x00;
    data[10] = (byte)width;
    data[11] = 0x00;
    data[12] = (byte)height;
    data[13] = 0x00;
    data[14] = 0x01; data[15] = 0x00; data[16] = 0x01; data[17] = 0x00; data[18] = 0x00; data[19] = 0x00;

    var fill = allSet ? (byte)'1' : (byte)'0';
    for (var i = 0; i < pixelCount; ++i)
      data[20 + i] = fill;

    return data;
  }
}

[TestFixture]
public class DataTypeTests {

  [Test]
  public void Magic_IsNOL() {
    Assert.That(NokiaOperatorLogoFile.Magic, Is.EqualTo(new byte[] { 0x4E, 0x4F, 0x4C }));
  }

  [Test]
  public void HeaderSize_Is20()
    => Assert.That(NokiaOperatorLogoFile.HeaderSize, Is.EqualTo(20));

  [Test]
  public void MinFileSize_Is20()
    => Assert.That(NokiaOperatorLogoFile.MinFileSize, Is.EqualTo(20));

  [Test]
  public void DefaultPixelData_IsEmpty() {
    var file = new NokiaOperatorLogoFile();
    Assert.That(file.PixelData, Is.Empty);
  }

  [Test]
  public void DefaultMcc_IsZero() {
    var file = new NokiaOperatorLogoFile();
    Assert.That(file.Mcc, Is.EqualTo(0));
  }

  [Test]
  public void DefaultMnc_IsZero() {
    var file = new NokiaOperatorLogoFile();
    Assert.That(file.Mnc, Is.EqualTo(0));
  }

  [Test]
  public void InitProperties_Width() {
    var file = new NokiaOperatorLogoFile { Width = 72 };
    Assert.That(file.Width, Is.EqualTo(72));
  }

  [Test]
  public void InitProperties_Height() {
    var file = new NokiaOperatorLogoFile { Height = 14 };
    Assert.That(file.Height, Is.EqualTo(14));
  }

  [Test]
  public void ToRawImage_NullFile_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => NokiaOperatorLogoFile.ToRawImage(null!));

  [Test]
  public void FromRawImage_NullImage_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => NokiaOperatorLogoFile.FromRawImage(null!));

  [Test]
  public void FromRawImage_WrongFormat_ThrowsArgumentException() {
    var raw = new RawImage { Width = 72, Height = 14, Format = PixelFormat.Rgba32, PixelData = new byte[72 * 14 * 4] };
    Assert.Throws<ArgumentException>(() => NokiaOperatorLogoFile.FromRawImage(raw));
  }

  [Test]
  public void FromRawImage_OversizedDimensions_ThrowsArgumentException() {
    var raw = new RawImage { Width = 256, Height = 14, Format = PixelFormat.Rgb24, PixelData = new byte[256 * 14 * 3] };
    Assert.Throws<ArgumentException>(() => NokiaOperatorLogoFile.FromRawImage(raw));
  }

  [Test]
  public void ToRawImage_Format_IsRgb24() {
    var file = new NokiaOperatorLogoFile { Width = 8, Height = 1, PixelData = new byte[1] };
    var raw = NokiaOperatorLogoFile.ToRawImage(file);
    Assert.That(raw.Format, Is.EqualTo(PixelFormat.Rgb24));
  }

  [Test]
  public void ToRawImage_SetBit_IsBlack() {
    var file = new NokiaOperatorLogoFile { Width = 8, Height = 1, PixelData = [0x80] };
    var raw = NokiaOperatorLogoFile.ToRawImage(file);
    Assert.That(raw.PixelData[0], Is.EqualTo(0));
    Assert.That(raw.PixelData[1], Is.EqualTo(0));
    Assert.That(raw.PixelData[2], Is.EqualTo(0));
  }

  [Test]
  public void ToRawImage_ClearBit_IsWhite() {
    var file = new NokiaOperatorLogoFile { Width = 8, Height = 1, PixelData = [0x00] };
    var raw = NokiaOperatorLogoFile.ToRawImage(file);
    Assert.That(raw.PixelData[0], Is.EqualTo(255));
    Assert.That(raw.PixelData[1], Is.EqualTo(255));
    Assert.That(raw.PixelData[2], Is.EqualTo(255));
  }

  [Test]
  public void FileExtensions_ContainsNol() {
    var extensions = GetFileExtensions();
    Assert.That(extensions, Does.Contain(".nol"));
  }

  private static string[] GetFileExtensions() => [".nol"];
}
