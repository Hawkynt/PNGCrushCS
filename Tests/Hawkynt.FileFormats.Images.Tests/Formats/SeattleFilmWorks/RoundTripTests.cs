using System;
using System.IO;
using FileFormat.SeattleFilmWorks;

namespace FileFormat.SeattleFilmWorks.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_JpegDataPreserved() {
    var jpegData = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46, 0xFF, 0xD9 };
    var original = new SeattleFilmWorksFile {
      JpegData = jpegData
    };

    var bytes = SeattleFilmWorksWriter.ToBytes(original);
    var restored = SeattleFilmWorksReader.FromBytes(bytes);

    Assert.That(restored.JpegData, Is.EqualTo(original.JpegData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_MinimalJpeg() {
    var jpegData = new byte[] { 0xFF, 0xD8, 0xFF, 0xD9 };
    var original = new SeattleFilmWorksFile {
      JpegData = jpegData
    };

    var bytes = SeattleFilmWorksWriter.ToBytes(original);
    var restored = SeattleFilmWorksReader.FromBytes(bytes);

    Assert.That(restored.JpegData, Is.EqualTo(original.JpegData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_LargeJpegData() {
    var jpegData = new byte[10000];
    jpegData[0] = 0xFF;
    jpegData[1] = 0xD8;
    for (var i = 2; i < jpegData.Length; ++i)
      jpegData[i] = (byte)(i * 13 % 256);
    jpegData[^2] = 0xFF;
    jpegData[^1] = 0xD9;

    var original = new SeattleFilmWorksFile {
      JpegData = jpegData
    };

    var bytes = SeattleFilmWorksWriter.ToBytes(original);
    var restored = SeattleFilmWorksReader.FromBytes(bytes);

    Assert.That(restored.JpegData, Is.EqualTo(original.JpegData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaFile() {
    var jpegData = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x02, 0xFF, 0xD9 };
    var original = new SeattleFilmWorksFile {
      JpegData = jpegData
    };

    var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".sfw");
    try {
      var bytes = SeattleFilmWorksWriter.ToBytes(original);
      File.WriteAllBytes(tempPath, bytes);

      var restored = SeattleFilmWorksReader.FromFile(new FileInfo(tempPath));

      Assert.That(restored.JpegData, Is.EqualTo(original.JpegData));
    } finally {
      if (File.Exists(tempPath))
        File.Delete(tempPath);
    }
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaRawImage() {
    var pixelData = new byte[10 * 10 * 3];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i % 256);

    var image = new FileFormat.Core.RawImage {
      Width = 10,
      Height = 10,
      Format = FileFormat.Core.PixelFormat.Rgb24,
      PixelData = pixelData,
    };

    var file = SeattleFilmWorksFile.FromRawImage(image);
    var rawImage = SeattleFilmWorksFile.ToRawImage(file);

    Assert.That(rawImage.Width, Is.EqualTo(10));
    Assert.That(rawImage.Height, Is.EqualTo(10));
    Assert.That(rawImage.Format, Is.EqualTo(FileFormat.Core.PixelFormat.Rgb24));
    Assert.That(rawImage.PixelData, Is.EqualTo(pixelData));
  }
}
