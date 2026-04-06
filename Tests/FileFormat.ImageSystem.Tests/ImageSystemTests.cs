using System;
using System.IO;
using FileFormat.Core;
using FileFormat.ImageSystem;

namespace FileFormat.ImageSystem.Tests;

[TestFixture]
public sealed class ImageSystemReaderTests {

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => ImageSystemReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".ish"));
    Assert.Throws<FileNotFoundException>(() => ImageSystemReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => ImageSystemReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_WrongSize_ThrowsInvalidDataException() {
    Assert.Throws<InvalidDataException>(() => ImageSystemReader.FromBytes(new byte[1000]));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_Hires_Parses() {
    var data = new byte[9009];
    data[0] = 0x00; data[1] = 0x20; // load address 0x2000

    var result = ImageSystemReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(320));
    Assert.That(result.Height, Is.EqualTo(200));
    Assert.That(result.IsHires, Is.True);
    Assert.That(result.LoadAddress, Is.EqualTo(0x2000));
    Assert.That(result.ColorData, Is.Null);
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_Multicolor_Parses() {
    var data = new byte[10003];
    data[0] = 0x00; data[1] = 0x60; // load address 0x6000

    var result = ImageSystemReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(160));
    Assert.That(result.Height, Is.EqualTo(200));
    Assert.That(result.IsHires, Is.False);
    Assert.That(result.LoadAddress, Is.EqualTo(0x6000));
    Assert.That(result.ColorData, Is.Not.Null);
  }
}

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_Hires_PreservesData() {
    var bitmapData = new byte[8000];
    bitmapData[0] = 0xFF;
    var screenData = new byte[1000];
    screenData[0] = 0x12;

    var original = new ImageSystemFile {
      Width = 320, Height = 200, IsHires = true,
      LoadAddress = 0x2000,
      BitmapData = bitmapData, ScreenData = screenData,
      BackgroundColor = 5,
    };

    var bytes = ImageSystemWriter.ToBytes(original);
    var restored = ImageSystemReader.FromBytes(bytes);

    Assert.That(restored.IsHires, Is.True);
    Assert.That(restored.LoadAddress, Is.EqualTo(0x2000));
    Assert.That(restored.BitmapData, Is.EqualTo(original.BitmapData));
    Assert.That(restored.ScreenData, Is.EqualTo(original.ScreenData));
    Assert.That(restored.BackgroundColor, Is.EqualTo(5));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_Multicolor_PreservesData() {
    var bitmapData = new byte[8000];
    bitmapData[0] = 0xAA;
    var screenData = new byte[1000];
    var colorData = new byte[1000];
    colorData[0] = 0x0F;

    var original = new ImageSystemFile {
      Width = 160, Height = 200, IsHires = false,
      LoadAddress = 0x6000,
      BitmapData = bitmapData, ScreenData = screenData,
      ColorData = colorData, BackgroundColor = 3,
    };

    var bytes = ImageSystemWriter.ToBytes(original);
    var restored = ImageSystemReader.FromBytes(bytes);

    Assert.That(restored.IsHires, Is.False);
    Assert.That(restored.BitmapData, Is.EqualTo(original.BitmapData));
    Assert.That(restored.ColorData, Is.EqualTo(original.ColorData));
    Assert.That(restored.BackgroundColor, Is.EqualTo(3));
  }
}

