using System;
using System.IO;
using FileFormat.Core;
using FileFormat.Mtv;
using Hawkynt.FileFormats.Images;

namespace FileFormat.Mtv.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_1x1() {
    var original = new MtvFile {
      Width = 1,
      Height = 1,
      PixelData = [42, 84, 126]
    };

    var bytes = MtvWriter.ToBytes(original);
    var restored = MtvReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_LargeImage() {
    var width = 64;
    var height = 32;
    var pixelData = new byte[width * height * 3];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 7 % 256);

    var original = new MtvFile {
      Width = width,
      Height = height,
      PixelData = pixelData
    };

    var bytes = MtvWriter.ToBytes(original);
    var restored = MtvReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(width));
    Assert.That(restored.Height, Is.EqualTo(height));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaFile() {
    var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".mtv");
    try {
      var original = new MtvFile {
        Width = 3,
        Height = 2,
        PixelData = [
          255, 0, 0, 0, 255, 0, 0, 0, 255,
          128, 128, 128, 64, 64, 64, 32, 32, 32
        ]
      };

      var bytes = MtvWriter.ToBytes(original);
      File.WriteAllBytes(tempPath, bytes);

      var restored = MtvReader.FromFile(new FileInfo(tempPath));

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
  public void RawImage_RoundTrip_IsLossless() {
    var source = new RawImage {
      Width = 3,
      Height = 2,
      Format = PixelFormat.Rgb24,
      PixelData = [
        0, 1, 2, 3, 4, 5, 6, 7, 8,
        250, 251, 252, 253, 254, 255, 17, 34, 51,
      ],
    };

    var encoded = MtvWriter.ToBytes(MtvFile.FromRawImage(source));
    var decoded = MtvFile.ToRawImage(MtvReader.FromBytes(encoded));

    Assert.Multiple(() => {
      Assert.That(decoded.Width, Is.EqualTo(source.Width));
      Assert.That(decoded.Height, Is.EqualTo(source.Height));
      Assert.That(decoded.Format, Is.EqualTo(PixelFormat.Rgb24));
      Assert.That(decoded.PixelData, Is.EqualTo(source.PixelData));
    });
  }

  [Test]
  [Category("Integration")]
  public void Registry_RegistersReadWritePicAliasAndStructuralDetection() {
    var format = FormatRegistry.DetectFromExtension(".mtv");
    var entry = FormatRegistry.GetEntry(format);
    var image = new RawImage {
      Width = 1,
      Height = 1,
      Format = PixelFormat.Rgb24,
      PixelData = [10, 20, 30],
    };

    Assert.That(format, Is.Not.EqualTo(ImageFormat.Unknown));
    Assert.That(entry, Is.Not.Null);
    Assert.Multiple(() => {
      Assert.That(entry!.SupportsRead, Is.True);
      Assert.That(entry.SupportsWrite, Is.True);
      Assert.That(entry.DetectionPriority, Is.EqualTo(999));
      Assert.That(entry.MimeTypes, Is.Empty);
      Assert.That(FormatRegistry.DetectCandidatesFromExtension(".pic"), Does.Contain(format));
    });

    var bytes = FormatRegistry.Write(image, format);
    Assert.That(bytes, Is.Not.Null);
    Assert.That(FormatRegistry.DetectFromBytes(bytes!), Is.EqualTo(format));

    var restored = FormatRegistry.Read(bytes!);
    Assert.That(restored, Is.Not.Null);
    Assert.That(restored!.PixelData, Is.EqualTo(image.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void Registry_TruncatedWeakSignature_IsNotClaimed() {
    var truncated = new byte[] { (byte)'1', (byte)'0', (byte)'0', (byte)'0', (byte)' ', (byte)'1', (byte)'0', (byte)'0', (byte)'0', (byte)'\n', 1, 2, 3 };

    Assert.That(FormatRegistry.DetectFromBytes(truncated), Is.Not.EqualTo(FormatRegistry.DetectFromExtension(".mtv")));
  }
}
