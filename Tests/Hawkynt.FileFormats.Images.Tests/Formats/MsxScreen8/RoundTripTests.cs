using System;
using System.IO;
using FileFormat.MsxScreen8;

namespace FileFormat.MsxScreen8.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_RawData_PreservesPixelData() {
    var pixelData = new byte[MsxScreen8File.PixelDataSize];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 7 % 256);

    var original = new MsxScreen8File {
      PixelData = pixelData,
      HasBsaveHeader = false
    };

    var bytes = MsxScreen8Writer.ToBytes(original);
    var restored = MsxScreen8Reader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(256));
    Assert.That(restored.Height, Is.EqualTo(212));
    Assert.That(restored.HasBsaveHeader, Is.False);
    Assert.That(restored.PixelData, Is.EqualTo(pixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_WithBsaveHeader_PreservesHeader() {
    var pixelData = new byte[MsxScreen8File.PixelDataSize];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 13 % 256);

    var original = new MsxScreen8File {
      PixelData = pixelData,
      HasBsaveHeader = true
    };

    var bytes = MsxScreen8Writer.ToBytes(original);
    var restored = MsxScreen8Reader.FromBytes(bytes);

    Assert.That(restored.HasBsaveHeader, Is.True);
    Assert.That(restored.PixelData, Is.EqualTo(pixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllZeros_PreservesData() {
    var original = new MsxScreen8File {
      PixelData = new byte[MsxScreen8File.PixelDataSize],
      HasBsaveHeader = false
    };

    var bytes = MsxScreen8Writer.ToBytes(original);
    var restored = MsxScreen8Reader.FromBytes(bytes);

    Assert.That(restored.PixelData, Is.All.EqualTo(0));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaFile_PreservesData() {
    var pixelData = new byte[MsxScreen8File.PixelDataSize];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 11 % 256);

    var original = new MsxScreen8File {
      PixelData = pixelData,
      HasBsaveHeader = false
    };

    var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".sc8");
    try {
      var bytes = MsxScreen8Writer.ToBytes(original);
      File.WriteAllBytes(tempPath, bytes);
      var restored = MsxScreen8Reader.FromFile(new FileInfo(tempPath));

      Assert.That(restored.Width, Is.EqualTo(256));
      Assert.That(restored.Height, Is.EqualTo(212));
      Assert.That(restored.PixelData, Is.EqualTo(pixelData));
    } finally {
      if (File.Exists(tempPath))
        File.Delete(tempPath);
    }
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ToRawImage_ProducesCorrectDimensions() {
    var file = new MsxScreen8File {
      PixelData = new byte[MsxScreen8File.PixelDataSize],
      HasBsaveHeader = false
    };

    var rawImage = MsxScreen8File.ToRawImage(file);

    Assert.That(rawImage.Width, Is.EqualTo(256));
    Assert.That(rawImage.Height, Is.EqualTo(212));
    Assert.That(rawImage.Format, Is.EqualTo(FileFormat.Core.PixelFormat.Rgb24));
    Assert.That(rawImage.PixelData.Length, Is.EqualTo(256 * 212 * 3));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ToRawImage_DecodesGrrbb() {
    var pixelData = new byte[MsxScreen8File.PixelDataSize];
    // Byte 0xFF = G=7, R=7, B=3 -> white
    pixelData[0] = 0xFF;

    var file = new MsxScreen8File {
      PixelData = pixelData,
      HasBsaveHeader = false
    };

    var rawImage = MsxScreen8File.ToRawImage(file);

    Assert.That(rawImage.PixelData[0], Is.EqualTo(255)); // R
    Assert.That(rawImage.PixelData[1], Is.EqualTo(255)); // G
    Assert.That(rawImage.PixelData[2], Is.EqualTo(255)); // B
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ToRawImage_DecodesBlack() {
    var pixelData = new byte[MsxScreen8File.PixelDataSize];
    pixelData[0] = 0x00; // G=0, R=0, B=0

    var file = new MsxScreen8File {
      PixelData = pixelData,
      HasBsaveHeader = false
    };

    var rawImage = MsxScreen8File.ToRawImage(file);

    Assert.That(rawImage.PixelData[0], Is.EqualTo(0)); // R
    Assert.That(rawImage.PixelData[1], Is.EqualTo(0)); // G
    Assert.That(rawImage.PixelData[2], Is.EqualTo(0)); // B
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_FromRawImage_QuantizesCorrectly() {
    var rawImage = new FileFormat.Core.RawImage {
      Width = 256,
      Height = 212,
      Format = FileFormat.Core.PixelFormat.Rgb24,
      PixelData = new byte[256 * 212 * 3]
    };
    rawImage.PixelData[0] = 255; // R
    rawImage.PixelData[1] = 255; // G
    rawImage.PixelData[2] = 255; // B

    var file = MsxScreen8File.FromRawImage(rawImage);

    Assert.That(file.PixelData[0], Is.EqualTo(0xFF)); // G=7, R=7, B=3
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaRawImage_PreservesExtremeValues() {
    var rawImage = new FileFormat.Core.RawImage {
      Width = 256,
      Height = 212,
      Format = FileFormat.Core.PixelFormat.Rgb24,
      PixelData = new byte[256 * 212 * 3]
    };

    var file = MsxScreen8File.FromRawImage(rawImage);
    var rawBack = MsxScreen8File.ToRawImage(file);

    Assert.That(rawBack.PixelData[0], Is.EqualTo(0));
    Assert.That(rawBack.PixelData[1], Is.EqualTo(0));
    Assert.That(rawBack.PixelData[2], Is.EqualTo(0));
  }
}
