using System;
using System.IO;
using FileFormat.Core;
using FileFormat.MsxVideo;

namespace FileFormat.MsxVideo.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_DataPreserved() {
    var pixels = new byte[54272];
    for (var i = 0; i < pixels.Length; ++i)
      pixels[i] = (byte)(i * 13 % 256);

    var original = new MsxVideoFile { PixelData = pixels };
    var bytes = MsxVideoWriter.ToBytes(original);
    var restored = MsxVideoReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllZeros() {
    var original = new MsxVideoFile { PixelData = new byte[54272] };

    var bytes = MsxVideoWriter.ToBytes(original);
    var restored = MsxVideoReader.FromBytes(bytes);

    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaFile() {
    var pixels = new byte[54272];
    for (var i = 0; i < pixels.Length; ++i)
      pixels[i] = (byte)(i * 7 % 256);

    var original = new MsxVideoFile { PixelData = pixels };
    var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".mvi");
    try {
      var bytes = MsxVideoWriter.ToBytes(original);
      File.WriteAllBytes(tempPath, bytes);

      var restored = MsxVideoReader.FromFile(new FileInfo(tempPath));

      Assert.That(restored.Width, Is.EqualTo(original.Width));
      Assert.That(restored.Height, Is.EqualTo(original.Height));
      Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
    } finally {
      if (File.Exists(tempPath))
        File.Delete(tempPath);
    }
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaRawImage() {
    var pixels = new byte[54272];
    for (var i = 0; i < pixels.Length; ++i)
      pixels[i] = (byte)(i * 11 % 256);

    var original = new MsxVideoFile { PixelData = pixels };
    var raw = MsxVideoFile.ToRawImage(original);

    Assert.That(raw.Format, Is.EqualTo(PixelFormat.Rgb24));
    Assert.That(raw.Width, Is.EqualTo(256));
    Assert.That(raw.Height, Is.EqualTo(212));

    var restored = MsxVideoFile.FromRawImage(raw);

    var raw2 = MsxVideoFile.ToRawImage(restored);
    Assert.That(raw2.PixelData, Is.EqualTo(raw.PixelData));
  }
}
