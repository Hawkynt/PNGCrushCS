using System;
using System.IO;
using FileFormat.Apng;
using FileFormat.Png;

namespace FileFormat.Apng.Tests;

[TestFixture]
public sealed class ApngReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => ApngReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => ApngReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var file = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".apng"));
    Assert.Throws<FileNotFoundException>(() => ApngReader.FromFile(file));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    var data = new byte[8];
    Assert.Throws<InvalidDataException>(() => ApngReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_InvalidSignature_ThrowsInvalidDataException() {
    var data = new byte[33];
    data[0] = 0xFF;
    Assert.Throws<InvalidDataException>(() => ApngReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidSingleFrame_ParsesCorrectly() {
    var frame = new ApngFrame {
      Width = 2,
      Height = 2,
      XOffset = 0,
      YOffset = 0,
      DelayNumerator = 100,
      DelayDenominator = 1000,
      DisposeOp = ApngDisposeOp.None,
      BlendOp = ApngBlendOp.Source,
      PixelData = [new byte[6], new byte[6]]
    };

    var original = new ApngFile {
      Width = 2,
      Height = 2,
      BitDepth = 8,
      ColorType = PngColorType.RGB,
      NumPlays = 0,
      Frames = [frame]
    };

    var bytes = ApngWriter.ToBytes(original);
    var result = ApngReader.FromBytes(bytes);

    Assert.That(result.Width, Is.EqualTo(2));
    Assert.That(result.Height, Is.EqualTo(2));
    Assert.That(result.BitDepth, Is.EqualTo(8));
    Assert.That(result.ColorType, Is.EqualTo(PngColorType.RGB));
    Assert.That(result.Frames, Has.Count.EqualTo(1));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidMultiFrame_ParsesAllFrames() {
    var frame0 = new ApngFrame {
      Width = 2,
      Height = 2,
      XOffset = 0,
      YOffset = 0,
      DelayNumerator = 100,
      DelayDenominator = 1000,
      DisposeOp = ApngDisposeOp.None,
      BlendOp = ApngBlendOp.Source,
      PixelData = [new byte[6], new byte[6]]
    };

    var frame1Pixels = new byte[2][];
    for (var y = 0; y < 2; ++y) {
      frame1Pixels[y] = new byte[6];
      for (var x = 0; x < 6; ++x)
        frame1Pixels[y][x] = (byte)(y * 6 + x + 10);
    }

    var frame1 = new ApngFrame {
      Width = 2,
      Height = 2,
      XOffset = 0,
      YOffset = 0,
      DelayNumerator = 200,
      DelayDenominator = 1000,
      DisposeOp = ApngDisposeOp.Background,
      BlendOp = ApngBlendOp.Over,
      PixelData = frame1Pixels
    };

    var original = new ApngFile {
      Width = 2,
      Height = 2,
      BitDepth = 8,
      ColorType = PngColorType.RGB,
      NumPlays = 1,
      Frames = [frame0, frame1]
    };

    var bytes = ApngWriter.ToBytes(original);
    var result = ApngReader.FromBytes(bytes);

    Assert.That(result.Frames, Has.Count.EqualTo(2));
    Assert.That(result.NumPlays, Is.EqualTo(1));
    Assert.That(result.Frames[0].DelayNumerator, Is.EqualTo(100));
    Assert.That(result.Frames[1].DelayNumerator, Is.EqualTo(200));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_NoActl_TreatedAsSingleFrame() {
    // Build a standard PNG (no acTL) using PngWriter
    var pngFile = new PngFile {
      Width = 2,
      Height = 2,
      BitDepth = 8,
      ColorType = PngColorType.RGB,
      PixelData = [new byte[6], new byte[6]]
    };

    var pngBytes = PngWriter.ToBytes(pngFile);
    var result = ApngReader.FromBytes(pngBytes);

    Assert.That(result.Width, Is.EqualTo(2));
    Assert.That(result.Height, Is.EqualTo(2));
    Assert.That(result.Frames, Has.Count.EqualTo(1));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_ValidApng_ParsesCorrectly() {
    var frame = new ApngFrame {
      Width = 2,
      Height = 2,
      XOffset = 0,
      YOffset = 0,
      DelayNumerator = 50,
      DelayDenominator = 1000,
      DisposeOp = ApngDisposeOp.None,
      BlendOp = ApngBlendOp.Source,
      PixelData = [new byte[6], new byte[6]]
    };

    var original = new ApngFile {
      Width = 2,
      Height = 2,
      BitDepth = 8,
      ColorType = PngColorType.RGB,
      NumPlays = 0,
      Frames = [frame]
    };

    var bytes = ApngWriter.ToBytes(original);
    using var stream = new MemoryStream(bytes);
    var result = ApngReader.FromStream(stream);

    Assert.That(result.Width, Is.EqualTo(2));
    Assert.That(result.Height, Is.EqualTo(2));
    Assert.That(result.Frames, Has.Count.EqualTo(1));
  }
}
