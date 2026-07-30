using System;
using System.IO;
using FileFormat.Core;
using FileFormat.MsxView;

namespace FileFormat.MsxView.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_DataPreserved() {
    var pixels = new byte[54272];
    for (var i = 0; i < pixels.Length; ++i)
      pixels[i] = (byte)(i * 13 % 256);

    var original = new MsxViewFile { PixelData = pixels };
    var bytes = MsxViewWriter.ToBytes(original);
    var restored = MsxViewReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllZeros() {
    var original = new MsxViewFile { PixelData = new byte[54272] };

    var bytes = MsxViewWriter.ToBytes(original);
    var restored = MsxViewReader.FromBytes(bytes);

    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaFile() {
    var pixels = new byte[54272];
    for (var i = 0; i < pixels.Length; ++i)
      pixels[i] = (byte)(i * 7 % 256);

    var original = new MsxViewFile { PixelData = pixels };
    var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".mvw");
    try {
      var bytes = MsxViewWriter.ToBytes(original);
      File.WriteAllBytes(tempPath, bytes);

      var restored = MsxViewReader.FromFile(new FileInfo(tempPath));

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

    var original = new MsxViewFile { PixelData = pixels };
    var raw = MsxViewFile.ToRawImage(original);

    Assert.That(raw.Format, Is.EqualTo(PixelFormat.Rgb24));
    Assert.That(raw.Width, Is.EqualTo(256));
    Assert.That(raw.Height, Is.EqualTo(212));

    var restored = MsxViewFile.FromRawImage(raw);

    var raw2 = MsxViewFile.ToRawImage(restored);
    Assert.That(raw2.PixelData, Is.EqualTo(raw.PixelData));
  }
}
