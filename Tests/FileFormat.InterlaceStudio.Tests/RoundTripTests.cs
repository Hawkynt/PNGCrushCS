using System;
using System.IO;
using FileFormat.InterlaceStudio;

namespace FileFormat.InterlaceStudio.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllZeros() {
    var original = _CreateValidFile();

    var bytes = InterlaceStudioWriter.ToBytes(original);
    var restored = InterlaceStudioReader.FromBytes(bytes);

    Assert.That(restored.LoadAddress, Is.EqualTo(original.LoadAddress));
    Assert.That(restored.Bitmap1, Is.EqualTo(original.Bitmap1));
    Assert.That(restored.Screen1, Is.EqualTo(original.Screen1));
    Assert.That(restored.ColorData, Is.EqualTo(original.ColorData));
    Assert.That(restored.Bitmap2, Is.EqualTo(original.Bitmap2));
    Assert.That(restored.Screen2, Is.EqualTo(original.Screen2));
    Assert.That(restored.BackgroundColor, Is.EqualTo(original.BackgroundColor));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_PatternData() {
    var original = _CreateValidFile();
    for (var i = 0; i < original.Bitmap1.Length; ++i)
      original.Bitmap1[i] = (byte)(i & 0xFF);
    for (var i = 0; i < original.Screen1.Length; ++i)
      original.Screen1[i] = (byte)((i * 3) & 0xFF);

    var bytes = InterlaceStudioWriter.ToBytes(original);
    var restored = InterlaceStudioReader.FromBytes(bytes);

    Assert.That(restored.Bitmap1, Is.EqualTo(original.Bitmap1));
    Assert.That(restored.Screen1, Is.EqualTo(original.Screen1));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllFields() {
    var original = _CreateValidFile(loadAddress: 0xBEEF, backgroundColor: 0x0A);
    for (var i = 0; i < original.Bitmap1.Length; ++i)
      original.Bitmap1[i] = (byte)(i & 0xFF);
    for (var i = 0; i < original.Screen1.Length; ++i)
      original.Screen1[i] = (byte)(i & 0xFF);
    for (var i = 0; i < original.ColorData.Length; ++i)
      original.ColorData[i] = (byte)(i & 0xFF);
    for (var i = 0; i < original.Bitmap2.Length; ++i)
      original.Bitmap2[i] = (byte)((i + 1) & 0xFF);
    for (var i = 0; i < original.Screen2.Length; ++i)
      original.Screen2[i] = (byte)((i + 2) & 0xFF);

    var bytes = InterlaceStudioWriter.ToBytes(original);
    var restored = InterlaceStudioReader.FromBytes(bytes);

    Assert.That(restored.LoadAddress, Is.EqualTo(0xBEEF));
    Assert.That(restored.Bitmap1, Is.EqualTo(original.Bitmap1));
    Assert.That(restored.Screen1, Is.EqualTo(original.Screen1));
    Assert.That(restored.ColorData, Is.EqualTo(original.ColorData));
    Assert.That(restored.Bitmap2, Is.EqualTo(original.Bitmap2));
    Assert.That(restored.Screen2, Is.EqualTo(original.Screen2));
    Assert.That(restored.BackgroundColor, Is.EqualTo(0x0A));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaFile() {
    var original = _CreateValidFile(loadAddress: 0x4000, backgroundColor: 0x03);
    for (var i = 0; i < original.Bitmap1.Length; ++i)
      original.Bitmap1[i] = (byte)(i & 0xFF);

    var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".ist");
    try {
      var bytes = InterlaceStudioWriter.ToBytes(original);
      File.WriteAllBytes(tempPath, bytes);

      var restored = InterlaceStudioReader.FromFile(new FileInfo(tempPath));

      Assert.That(restored.LoadAddress, Is.EqualTo(original.LoadAddress));
      Assert.That(restored.Bitmap1, Is.EqualTo(original.Bitmap1));
      Assert.That(restored.BackgroundColor, Is.EqualTo(original.BackgroundColor));
    } finally {
      if (File.Exists(tempPath))
        File.Delete(tempPath);
    }
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ToRawImage_ReturnsRgb24() {
    var original = _CreateValidFile();

    var raw = InterlaceStudioFile.ToRawImage(original);

    Assert.That(raw.Width, Is.EqualTo(InterlaceStudioFile.ImageWidth));
    Assert.That(raw.Height, Is.EqualTo(InterlaceStudioFile.ImageHeight));
    Assert.That(raw.Format, Is.EqualTo(FileFormat.Core.PixelFormat.Rgb24));
    Assert.That(raw.PixelData.Length, Is.EqualTo(160 * 200 * 3));
  }

  private static InterlaceStudioFile _CreateValidFile(ushort loadAddress = 0x4000, byte backgroundColor = 0) => new() {
    LoadAddress = loadAddress,
    Bitmap1 = new byte[InterlaceStudioFile.BitmapDataSize],
    Screen1 = new byte[InterlaceStudioFile.ScreenDataSize],
    ColorData = new byte[InterlaceStudioFile.ColorDataSize],
    Bitmap2 = new byte[InterlaceStudioFile.BitmapDataSize],
    Screen2 = new byte[InterlaceStudioFile.ScreenDataSize],
    BackgroundColor = backgroundColor,
  };
}
