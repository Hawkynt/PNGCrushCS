using System;
using System.IO;
using FileFormat.AdvancedArtStudio;
using FileFormat.Core;

namespace FileFormat.AdvancedArtStudio.Tests;

[TestFixture]
public sealed class AdvancedArtStudioReaderTests {

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => AdvancedArtStudioReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".ocp"));
    Assert.Throws<FileNotFoundException>(() => AdvancedArtStudioReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => AdvancedArtStudioReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    Assert.Throws<InvalidDataException>(() => AdvancedArtStudioReader.FromBytes(new byte[100]));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_HiResLayout_ParsesAs320x200() {
    var data = new byte[AdvancedArtStudioFile.HiResFileSize];
    data[0] = 0x00; data[1] = 0x20;       // load address $2000
    for (var i = 0; i < 8000; ++i) data[2 + i] = (byte)(i & 0xFF);
    for (var i = 0; i < 1000; ++i) data[8002 + i] = (byte)((i % 16) << 4 | ((i + 1) % 16));
    data[^1] = 0x06;                       // border colour in last byte

    var result = AdvancedArtStudioReader.FromBytes(data);

    Assert.That(result.IsHiRes, Is.True);
    Assert.That(result.Width, Is.EqualTo(320));
    Assert.That(result.Height, Is.EqualTo(200));
    Assert.That(result.BitmapData.Length, Is.EqualTo(8000));
    Assert.That(result.ScreenRam.Length, Is.EqualTo(1000));
    Assert.That(result.ColorRam, Is.Empty);
    Assert.That(result.BorderColor, Is.EqualTo(0x06));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidData_ParsesDimensions() {
    var data = TestHelpers._BuildValidFile(0x2000, 0x03, 0x01);
    var result = AdvancedArtStudioReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(160));
    Assert.That(result.Height, Is.EqualTo(200));
    Assert.That(result.LoadAddress, Is.EqualTo(0x2000));
    Assert.That(result.BitmapData.Length, Is.EqualTo(8000));
    Assert.That(result.ScreenRam.Length, Is.EqualTo(1000));
    Assert.That(result.ColorRam.Length, Is.EqualTo(1000));
    Assert.That(result.BackgroundColor, Is.EqualTo(0x03));
    Assert.That(result.BorderColor, Is.EqualTo(0x01));
  }
}

[TestFixture]
public sealed class AdvancedArtStudioRoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_HiRes_AllFieldsPreserved() {
    var bitmap = new byte[8000];
    for (var i = 0; i < bitmap.Length; ++i) bitmap[i] = (byte)(i * 5 % 256);
    var screen = new byte[1000];
    for (var i = 0; i < screen.Length; ++i) screen[i] = (byte)((i * 11 + 1) & 0xFF);

    var original = new AdvancedArtStudioFile {
      IsHiRes = true,
      LoadAddress = 0x2000,
      BitmapData = bitmap,
      ScreenRam = screen,
      ColorRam = [],
      BorderColor = 4,
    };
    var bytes = AdvancedArtStudioWriter.ToBytes(original);
    Assert.That(bytes.Length, Is.EqualTo(AdvancedArtStudioFile.HiResFileSize));

    var restored = AdvancedArtStudioReader.FromBytes(bytes);
    Assert.That(restored.IsHiRes, Is.True);
    Assert.That(restored.LoadAddress, Is.EqualTo(original.LoadAddress));
    Assert.That(restored.BitmapData, Is.EqualTo(original.BitmapData));
    Assert.That(restored.ScreenRam, Is.EqualTo(original.ScreenRam));
    Assert.That(restored.BorderColor, Is.EqualTo(original.BorderColor));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllFieldsPreserved() {
    var bitmapData = new byte[8000];
    for (var i = 0; i < bitmapData.Length; ++i)
      bitmapData[i] = (byte)(i * 7 % 256);

    var screenRam = new byte[1000];
    for (var i = 0; i < screenRam.Length; ++i)
      screenRam[i] = (byte)(i % 16);

    var colorRam = new byte[1000];
    for (var i = 0; i < colorRam.Length; ++i)
      colorRam[i] = (byte)((i * 3 + 1) % 16);

    var original = new AdvancedArtStudioFile {
      LoadAddress = 0x2000,
      BitmapData = bitmapData,
      ScreenRam = screenRam,
      ColorRam = colorRam,
      BackgroundColor = 11,
      BorderColor = 5,
    };

    var bytes = AdvancedArtStudioWriter.ToBytes(original);
    var restored = AdvancedArtStudioReader.FromBytes(bytes);

    Assert.That(restored.LoadAddress, Is.EqualTo(original.LoadAddress));
    Assert.That(restored.BitmapData, Is.EqualTo(original.BitmapData));
    Assert.That(restored.ScreenRam, Is.EqualTo(original.ScreenRam));
    Assert.That(restored.ColorRam, Is.EqualTo(original.ColorRam));
    Assert.That(restored.BackgroundColor, Is.EqualTo(original.BackgroundColor));
    Assert.That(restored.BorderColor, Is.EqualTo(original.BorderColor));
  }
}

file class TestHelpers {
  internal static byte[] _BuildValidFile(ushort loadAddress, byte backgroundColor, byte borderColor) {
    var data = new byte[AdvancedArtStudioFile.ExpectedFileSize];
    data[0] = (byte)(loadAddress & 0xFF);
    data[1] = (byte)(loadAddress >> 8);

    for (var i = 0; i < 8000; ++i)
      data[2 + i] = (byte)(i % 256);

    for (var i = 0; i < 1000; ++i)
      data[8002 + i] = (byte)(i % 16);

    for (var i = 0; i < 1000; ++i)
      data[9002 + i] = (byte)((i + 3) % 16);

    data[10002] = backgroundColor;
    data[10003] = borderColor;

    return data;
  }
}
