using System;
using System.IO;
using FileFormat.Core;
using FileFormat.Jpeg;
using FileFormat.Mpo;

namespace FileFormat.Mpo.Tests;

[TestFixture]
public sealed class RoundTripTests {

  private static byte[] _CreateMinimalJpegBytes(int width = 4, int height = 4) {
    var rgb = new byte[width * height * 3];
    for (var i = 0; i < rgb.Length; ++i)
      rgb[i] = (byte)(i * 7 % 256);

    var jpeg = new JpegFile {
      Width = width,
      Height = height,
      IsGrayscale = false,
      RgbPixelData = rgb,
    };
    return JpegWriter.ToBytes(jpeg);
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_SingleImage() {
    var jpegBytes = _CreateMinimalJpegBytes(8, 8);
    var original = new MpoFile { Images = [jpegBytes] };

    var bytes = MpoWriter.ToBytes(original);
    var restored = MpoReader.FromBytes(bytes);

    Assert.That(restored.Images.Count, Is.EqualTo(1));
    var decoded = JpegReader.FromBytes(restored.Images[0]);
    Assert.That(decoded.Width, Is.EqualTo(8));
    Assert.That(decoded.Height, Is.EqualTo(8));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_MultipleImages() {
    var jpeg1 = _CreateMinimalJpegBytes(4, 4);
    var jpeg2 = _CreateMinimalJpegBytes(8, 8);
    var original = new MpoFile { Images = [jpeg1, jpeg2] };

    var bytes = MpoWriter.ToBytes(original);
    var restored = MpoReader.FromBytes(bytes);

    Assert.That(restored.Images.Count, Is.EqualTo(2));

    var decoded1 = JpegReader.FromBytes(restored.Images[0]);
    Assert.That(decoded1.Width, Is.EqualTo(4));
    Assert.That(decoded1.Height, Is.EqualTo(4));

    var decoded2 = JpegReader.FromBytes(restored.Images[1]);
    Assert.That(decoded2.Width, Is.EqualTo(8));
    Assert.That(decoded2.Height, Is.EqualTo(8));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ThreeImages() {
    var jpeg1 = _CreateMinimalJpegBytes(4, 4);
    var jpeg2 = _CreateMinimalJpegBytes(8, 8);
    var jpeg3 = _CreateMinimalJpegBytes(16, 16);
    var original = new MpoFile { Images = [jpeg1, jpeg2, jpeg3] };

    var bytes = MpoWriter.ToBytes(original);
    var restored = MpoReader.FromBytes(bytes);

    Assert.That(restored.Images.Count, Is.EqualTo(3));

    var decoded3 = JpegReader.FromBytes(restored.Images[2]);
    Assert.That(decoded3.Width, Is.EqualTo(16));
    Assert.That(decoded3.Height, Is.EqualTo(16));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaFile() {
    var jpegBytes = _CreateMinimalJpegBytes(8, 8);
    var original = new MpoFile { Images = [jpegBytes] };
    var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".mpo");

    try {
      var bytes = MpoWriter.ToBytes(original);
      File.WriteAllBytes(tempPath, bytes);

      var restored = MpoReader.FromFile(new FileInfo(tempPath));

      Assert.That(restored.Images.Count, Is.EqualTo(1));
      var decoded = JpegReader.FromBytes(restored.Images[0]);
      Assert.That(decoded.Width, Is.EqualTo(8));
      Assert.That(decoded.Height, Is.EqualTo(8));
    } finally {
      if (File.Exists(tempPath))
        File.Delete(tempPath);
    }
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaRawImage() {
    var rawImage = new RawImage {
      Width = 4,
      Height = 4,
      Format = PixelFormat.Rgb24,
      PixelData = new byte[4 * 4 * 3]
    };
    for (var i = 0; i < rawImage.PixelData.Length; ++i)
      rawImage.PixelData[i] = (byte)(i * 11 % 256);

    var mpo = MpoFile.FromRawImage(rawImage);
    var bytes = MpoWriter.ToBytes(mpo);
    var restored = MpoReader.FromBytes(bytes);
    var rawBack = MpoFile.ToRawImage(restored);

    Assert.That(rawBack.Width, Is.EqualTo(4));
    Assert.That(rawBack.Height, Is.EqualTo(4));
    Assert.That(rawBack.Format, Is.EqualTo(PixelFormat.Rgb24));
    Assert.That(rawBack.PixelData.Length, Is.EqualTo(4 * 4 * 3));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_MultipleImages_ViaFile() {
    var jpeg1 = _CreateMinimalJpegBytes(4, 4);
    var jpeg2 = _CreateMinimalJpegBytes(8, 8);
    var original = new MpoFile { Images = [jpeg1, jpeg2] };
    var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".mpo");

    try {
      var bytes = MpoWriter.ToBytes(original);
      File.WriteAllBytes(tempPath, bytes);

      var restored = MpoReader.FromFile(new FileInfo(tempPath));

      Assert.That(restored.Images.Count, Is.EqualTo(2));

      var decoded1 = JpegReader.FromBytes(restored.Images[0]);
      Assert.That(decoded1.Width, Is.EqualTo(4));

      var decoded2 = JpegReader.FromBytes(restored.Images[1]);
      Assert.That(decoded2.Width, Is.EqualTo(8));
    } finally {
      if (File.Exists(tempPath))
        File.Delete(tempPath);
    }
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_GrayscaleImage() {
    var gray = new byte[8 * 8];
    for (var i = 0; i < gray.Length; ++i)
      gray[i] = (byte)(i * 3 % 256);

    var jpeg = new JpegFile {
      Width = 8,
      Height = 8,
      IsGrayscale = true,
      RgbPixelData = gray,
    };
    var jpegBytes = JpegWriter.ToBytes(jpeg);

    var original = new MpoFile { Images = [jpegBytes] };
    var bytes = MpoWriter.ToBytes(original);
    var restored = MpoReader.FromBytes(bytes);

    Assert.That(restored.Images.Count, Is.EqualTo(1));
    var decoded = JpegReader.FromBytes(restored.Images[0]);
    Assert.That(decoded.Width, Is.EqualTo(8));
    Assert.That(decoded.Height, Is.EqualTo(8));
    Assert.That(decoded.IsGrayscale, Is.True);
  }
}
