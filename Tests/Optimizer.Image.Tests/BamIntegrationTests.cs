using System.Buffers.Binary;
using System.IO;
using FileFormat.Bam;
using FileFormat.Core;

namespace Optimizer.Image.Tests;

/// <summary>Integration tests that verify the new BAM format is fully wired into FormatRegistry,
/// ImageFormatDetector, and FormatIO — proving the new architecture makes adding a format trivial.</summary>
[TestFixture]
public sealed class BamIntegrationTests {

  private static byte[] _MakeBamBytes(int width, int height) {
    var pixelBytes = width * height * 4;
    var result = new byte[16 + pixelBytes];
    result[0] = (byte)'B'; result[1] = (byte)'A'; result[2] = (byte)'M'; result[3] = (byte)'F';
    BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(4), 1u);
    BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(8), (uint)width);
    BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(12), (uint)height);
    for (var i = 0; i < pixelBytes; ++i)
      result[16 + i] = (byte)((i * 17) & 0xFF);
    return result;
  }

  [Test]
  public void ImageFormat_Bam_EnumValueExists() {
    // The source generator must have discovered BamFile and added it to the enum
    Assert.That(System.Enum.TryParse<ImageFormat>("Bam", out var bam), Is.True);
    Assert.That(bam, Is.Not.EqualTo(ImageFormat.Unknown));
  }

  [Test]
  public void DetectFromExtension_BamExtension_ReturnsBam() {
    var fi = new FileInfo("foo.bam");
    Assert.That(ImageFormatDetector.DetectFromExtension(fi), Is.EqualTo(ImageFormat.Bam));
  }

  [Test]
  public void DetectFromSignature_BamMagic_ReturnsBam() {
    var bytes = _MakeBamBytes(2, 2);
    Assert.That(ImageFormatDetector.DetectFromSignature(bytes), Is.EqualTo(ImageFormat.Bam));
  }

  [Test]
  public void BitmapConverter_LoadRawImage_FromBamFile_Works() {
    var bytes = _MakeBamBytes(4, 3);
    var tempPath = Path.GetTempFileName();
    try {
      File.WriteAllBytes(tempPath, bytes);
      var raw = BitmapConverter.LoadRawImage(new FileInfo(tempPath), ImageFormat.Bam);
      Assert.That(raw, Is.Not.Null);
      Assert.That(raw!.Width, Is.EqualTo(4));
      Assert.That(raw.Height, Is.EqualTo(3));
      Assert.That(raw.Format, Is.EqualTo(PixelFormat.Rgba32));
    } finally {
      File.Delete(tempPath);
    }
  }

  [Test]
  public void FormatIO_FullPipeline_BamRoundTrip() {
    // Encode raw → BAM bytes → decode → compare
    var raw = new RawImage {
      Width = 5,
      Height = 4,
      Format = PixelFormat.Rgba32,
      PixelData = new byte[5 * 4 * 4],
    };
    for (var i = 0; i < raw.PixelData.Length; ++i)
      raw.PixelData[i] = (byte)((i * 13) & 0xFF);

    var encoded = FormatIO.Encode<BamFile>(raw);
    var decoded = FormatIO.Decode<BamFile>(encoded);

    Assert.That(decoded.Width, Is.EqualTo(5));
    Assert.That(decoded.Height, Is.EqualTo(4));
    Assert.That(decoded.Format, Is.EqualTo(PixelFormat.Rgba32));
    Assert.That(decoded.PixelData, Is.EqualTo(raw.PixelData));
  }

  [Test]
  public void FormatIO_ReadInfo_ReadsBamMetadataWithoutDecodingPixels() {
    var bytes = _MakeBamBytes(100, 50);
    var tempPath = Path.GetTempFileName();
    try {
      File.WriteAllBytes(tempPath, bytes);
      var info = FormatIO.ReadInfo<BamFile>(new FileInfo(tempPath));
      Assert.That(info, Is.Not.Null);
      Assert.That(info!.Value.Width, Is.EqualTo(100));
      Assert.That(info.Value.Height, Is.EqualTo(50));
      Assert.That(info.Value.BitsPerPixel, Is.EqualTo(32));
    } finally {
      File.Delete(tempPath);
    }
  }
}
