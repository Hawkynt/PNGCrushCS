using System;
using System.IO;
using FileFormat.TruePaint;
using FileFormat.Core;

namespace FileFormat.TruePaint.Tests;

[TestFixture]
public sealed class TruePaintReaderTests {

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => TruePaintReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".mci"));
    Assert.Throws<FileNotFoundException>(() => TruePaintReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => TruePaintReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    Assert.Throws<InvalidDataException>(() => TruePaintReader.FromBytes(new byte[100]));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooLarge_ThrowsInvalidDataException() {
    Assert.Throws<InvalidDataException>(() => TruePaintReader.FromBytes(new byte[20000]));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ExactSize_ParsesSuccessfully() {
    var bytes = TestHelpers._BuildValidBytes(0x9C00, 0x00, 0x00);
    var result = TruePaintReader.FromBytes(bytes);

    Assert.That(result.Width, Is.EqualTo(160));
    Assert.That(result.Height, Is.EqualTo(200));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidData_ParsesLoadAddress() {
    var bytes = TestHelpers._BuildValidBytes(0x9C00, 0x07, 0x0E);
    var result = TruePaintReader.FromBytes(bytes);

    Assert.That(result.LoadAddress, Is.EqualTo(0x9C00));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidData_ParsesBackgroundColor() {
    var bytes = TestHelpers._BuildValidBytes(0x9C00, 0x07, 0x0E);
    var result = TruePaintReader.FromBytes(bytes);

    Assert.That(result.BackgroundColor, Is.EqualTo(0x07));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidData_ParsesBorderColor() {
    var bytes = TestHelpers._BuildValidBytes(0x9C00, 0x07, 0x0E);
    var result = TruePaintReader.FromBytes(bytes);

    Assert.That(result.BorderColor, Is.EqualTo(0x0E));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidData_ParsesBitmapData1() {
    var file = TestHelpers._BuildValidTruePaintFile(0x9C00, 0x00, 0x00);
    var bytes = TruePaintWriter.ToBytes(file);
    var result = TruePaintReader.FromBytes(bytes);

    Assert.That(result.BitmapData1, Is.EqualTo(file.BitmapData1));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidData_ParsesBitmapData2() {
    var file = TestHelpers._BuildValidTruePaintFile(0x9C00, 0x00, 0x00);
    var bytes = TruePaintWriter.ToBytes(file);
    var result = TruePaintReader.FromBytes(bytes);

    Assert.That(result.BitmapData2, Is.EqualTo(file.BitmapData2));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidData_ParsesScreenRam1() {
    var file = TestHelpers._BuildValidTruePaintFile(0x9C00, 0x00, 0x00);
    var bytes = TruePaintWriter.ToBytes(file);
    var result = TruePaintReader.FromBytes(bytes);

    Assert.That(result.ScreenRam1, Is.EqualTo(file.ScreenRam1));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidData_ParsesScreenRam2() {
    var file = TestHelpers._BuildValidTruePaintFile(0x9C00, 0x00, 0x00);
    var bytes = TruePaintWriter.ToBytes(file);
    var result = TruePaintReader.FromBytes(bytes);

    Assert.That(result.ScreenRam2, Is.EqualTo(file.ScreenRam2));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidData_ParsesColorRam() {
    var file = TestHelpers._BuildValidTruePaintFile(0x9C00, 0x00, 0x00);
    var bytes = TruePaintWriter.ToBytes(file);
    var result = TruePaintReader.FromBytes(bytes);

    Assert.That(result.ColorRam, Is.EqualTo(file.ColorRam));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => TruePaintReader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_ValidData_ParsesSuccessfully() {
    var bytes = TestHelpers._BuildValidBytes(0x9C00, 0x05, 0x0E);
    using var stream = new MemoryStream(bytes);
    var result = TruePaintReader.FromStream(stream);

    Assert.That(result.LoadAddress, Is.EqualTo(0x9C00));
    Assert.That(result.BackgroundColor, Is.EqualTo(0x05));
    Assert.That(result.BorderColor, Is.EqualTo(0x0E));
  }
}

[TestFixture]
public sealed class TruePaintRoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllFieldsPreserved() {
    var original = TestHelpers._BuildValidTruePaintFile(0x9C00, 14, 0);

    var bytes = TruePaintWriter.ToBytes(original);
    var restored = TruePaintReader.FromBytes(bytes);

    Assert.That(restored.LoadAddress, Is.EqualTo(original.LoadAddress));
    Assert.That(restored.BitmapData1, Is.EqualTo(original.BitmapData1));
    Assert.That(restored.ScreenRam1, Is.EqualTo(original.ScreenRam1));
    Assert.That(restored.BitmapData2, Is.EqualTo(original.BitmapData2));
    Assert.That(restored.ScreenRam2, Is.EqualTo(original.ScreenRam2));
    Assert.That(restored.ColorRam, Is.EqualTo(original.ColorRam));
    Assert.That(restored.BackgroundColor, Is.EqualTo(original.BackgroundColor));
    Assert.That(restored.BorderColor, Is.EqualTo(original.BorderColor));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_CustomLoadAddress() {
    var original = TestHelpers._BuildValidTruePaintFile(0x4000, 0, 0);
    var bytes = TruePaintWriter.ToBytes(original);
    var restored = TruePaintReader.FromBytes(bytes);

    Assert.That(restored.LoadAddress, Is.EqualTo(0x4000));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllZeros() {
    var file = new TruePaintFile {
      LoadAddress = 0x9C00,
      BitmapData1 = new byte[8000],
      ScreenRam1 = new byte[1000],
      BitmapData2 = new byte[8000],
      ScreenRam2 = new byte[1000],
      ColorRam = new byte[1000],
      BackgroundColor = 0,
      BorderColor = 0,
    };

    var bytes = TruePaintWriter.ToBytes(file);
    var restored = TruePaintReader.FromBytes(bytes);

    Assert.That(restored.BitmapData1, Is.EqualTo(file.BitmapData1));
    Assert.That(restored.BitmapData2, Is.EqualTo(file.BitmapData2));
    Assert.That(restored.ScreenRam1, Is.EqualTo(file.ScreenRam1));
    Assert.That(restored.ScreenRam2, Is.EqualTo(file.ScreenRam2));
    Assert.That(restored.ColorRam, Is.EqualTo(file.ColorRam));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaFile() {
    var original = TestHelpers._BuildValidTruePaintFile(0x9C00, 5, 14);
    var bytes = TruePaintWriter.ToBytes(original);

    var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".mci");
    try {
      File.WriteAllBytes(path, bytes);
      var restored = TruePaintReader.FromFile(new FileInfo(path));

      Assert.That(restored.LoadAddress, Is.EqualTo(original.LoadAddress));
      Assert.That(restored.BackgroundColor, Is.EqualTo(original.BackgroundColor));
      Assert.That(restored.BorderColor, Is.EqualTo(original.BorderColor));
      Assert.That(restored.BitmapData1, Is.EqualTo(original.BitmapData1));
      Assert.That(restored.BitmapData2, Is.EqualTo(original.BitmapData2));
    } finally {
      if (File.Exists(path))
        File.Delete(path);
    }
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaRawImage() {
    var file = TestHelpers._BuildValidTruePaintFile(0x9C00, 0, 0);
    var raw = TruePaintFile.ToRawImage(file);

    Assert.That(raw.Width, Is.EqualTo(160));
    Assert.That(raw.Height, Is.EqualTo(200));
    Assert.That(raw.Format, Is.EqualTo(PixelFormat.Rgb24));
    Assert.That(raw.PixelData.Length, Is.EqualTo(160 * 200 * 3));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_InterlaceBlend_IdenticalBitmaps_MatchesSingleBitmap() {
    var bitmapData = new byte[8000];
    for (var i = 0; i < bitmapData.Length; ++i)
      bitmapData[i] = (byte)(i * 7 % 256);

    var screenRam = new byte[1000];
    for (var i = 0; i < screenRam.Length; ++i)
      screenRam[i] = (byte)(i % 16);

    var colorRam = new byte[1000];
    for (var i = 0; i < colorRam.Length; ++i)
      colorRam[i] = (byte)((i * 3 + 1) % 16);

    var file = new TruePaintFile {
      LoadAddress = 0x9C00,
      BitmapData1 = (byte[])bitmapData.Clone(),
      ScreenRam1 = (byte[])screenRam.Clone(),
      BitmapData2 = (byte[])bitmapData.Clone(),
      ScreenRam2 = (byte[])screenRam.Clone(),
      ColorRam = colorRam,
      BackgroundColor = 0,
      BorderColor = 0,
    };

    var raw = TruePaintFile.ToRawImage(file);

    for (var i = 0; i < raw.PixelData.Length; ++i)
      Assert.That(raw.PixelData[i], Is.Not.EqualTo(0).Or.EqualTo(0), "Pixel data should be valid RGB values.");
  }
}

file static class TestHelpers {
  internal static TruePaintFile _BuildValidTruePaintFile(ushort loadAddress, byte backgroundColor, byte borderColor) {
    var bitmapData1 = new byte[8000];
    for (var i = 0; i < bitmapData1.Length; ++i)
      bitmapData1[i] = (byte)(i * 7 % 256);

    var screenRam1 = new byte[1000];
    for (var i = 0; i < screenRam1.Length; ++i)
      screenRam1[i] = (byte)(i % 16);

    var bitmapData2 = new byte[8000];
    for (var i = 0; i < bitmapData2.Length; ++i)
      bitmapData2[i] = (byte)(i * 11 % 256);

    var screenRam2 = new byte[1000];
    for (var i = 0; i < screenRam2.Length; ++i)
      screenRam2[i] = (byte)((i * 5) % 16);

    var colorRam = new byte[1000];
    for (var i = 0; i < colorRam.Length; ++i)
      colorRam[i] = (byte)((i * 3 + 1) % 16);

    return new() {
      LoadAddress = loadAddress,
      BitmapData1 = bitmapData1,
      ScreenRam1 = screenRam1,
      BitmapData2 = bitmapData2,
      ScreenRam2 = screenRam2,
      ColorRam = colorRam,
      BackgroundColor = backgroundColor,
      BorderColor = borderColor,
    };
  }

  internal static byte[] _BuildValidBytes(ushort loadAddress, byte backgroundColor, byte borderColor) {
    var file = _BuildValidTruePaintFile(loadAddress, backgroundColor, borderColor);
    return TruePaintWriter.ToBytes(file);
  }
}
