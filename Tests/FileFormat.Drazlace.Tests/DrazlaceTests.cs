using System;
using System.IO;
using FileFormat.Drazlace;
using FileFormat.Core;

namespace FileFormat.Drazlace.Tests;

[TestFixture]
public sealed class DrazlaceReaderTests {

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => DrazlaceReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".dlp"));
    Assert.Throws<FileNotFoundException>(() => DrazlaceReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => DrazlaceReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    Assert.Throws<InvalidDataException>(() => DrazlaceReader.FromBytes(new byte[2]));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidData_ParsesDimensions() {
    var file = TestHelpers._BuildValidDrazlaceFile(0x5800, 0x07);
    var bytes = DrazlaceWriter.ToBytes(file);
    var result = DrazlaceReader.FromBytes(bytes);

    Assert.That(result.Width, Is.EqualTo(160));
    Assert.That(result.Height, Is.EqualTo(200));
    Assert.That(result.LoadAddress, Is.EqualTo(0x5800));
    Assert.That(result.BackgroundColor, Is.EqualTo(0x07));
  }
}

[TestFixture]
public sealed class DrazlaceRoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllFieldsPreserved() {
    var original = TestHelpers._BuildValidDrazlaceFile(0x5800, 14);

    var bytes = DrazlaceWriter.ToBytes(original);
    var restored = DrazlaceReader.FromBytes(bytes);

    Assert.That(restored.LoadAddress, Is.EqualTo(original.LoadAddress));
    Assert.That(restored.BitmapData1, Is.EqualTo(original.BitmapData1));
    Assert.That(restored.ScreenRam1, Is.EqualTo(original.ScreenRam1));
    Assert.That(restored.ColorRam, Is.EqualTo(original.ColorRam));
    Assert.That(restored.BackgroundColor, Is.EqualTo(original.BackgroundColor));
    Assert.That(restored.BitmapData2, Is.EqualTo(original.BitmapData2));
    Assert.That(restored.ScreenRam2, Is.EqualTo(original.ScreenRam2));
  }
}

file static class TestHelpers {
  internal static DrazlaceFile _BuildValidDrazlaceFile(ushort loadAddress, byte backgroundColor) {
    var bitmapData1 = new byte[8000];
    for (var i = 0; i < bitmapData1.Length; ++i)
      bitmapData1[i] = (byte)(i * 7 % 256);

    var screenRam1 = new byte[1000];
    for (var i = 0; i < screenRam1.Length; ++i)
      screenRam1[i] = (byte)(i % 16);

    var colorRam = new byte[1000];
    for (var i = 0; i < colorRam.Length; ++i)
      colorRam[i] = (byte)((i * 3 + 1) % 16);

    var bitmapData2 = new byte[8000];
    for (var i = 0; i < bitmapData2.Length; ++i)
      bitmapData2[i] = (byte)(i * 11 % 256);

    var screenRam2 = new byte[1000];
    for (var i = 0; i < screenRam2.Length; ++i)
      screenRam2[i] = (byte)((i * 5 + 2) % 16);

    return new() {
      LoadAddress = loadAddress,
      BitmapData1 = bitmapData1,
      ScreenRam1 = screenRam1,
      ColorRam = colorRam,
      BackgroundColor = backgroundColor,
      BitmapData2 = bitmapData2,
      ScreenRam2 = screenRam2,
    };
  }
}
