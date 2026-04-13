using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.Bam;
using FileFormat.Core;

namespace Optimizer.Image.Tests;

/// <summary>Tests for the critical code path used by Crush.Viewer — the pipeline that loads an image,
/// detects its format, decodes it to RawImage, and produces a Bitmap for display.</summary>
[TestFixture]
public sealed class ViewerPipelineTests {

  private static byte[] _MakeBam(int width, int height) {
    var pixelBytes = width * height * 4;
    var result = new byte[16 + pixelBytes];
    result[0] = (byte)'B'; result[1] = (byte)'A'; result[2] = (byte)'M'; result[3] = (byte)'F';
    BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(4), 1u);
    BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(8), (uint)width);
    BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(12), (uint)height);
    for (var i = 0; i < pixelBytes; ++i)
      result[16 + i] = (byte)((i * 7) & 0xFF);
    return result;
  }

  // ── Step 1: Detect ────────────────────────────────────────────────────────

  [Test]
  public void ViewerStep_ImageFormatDetector_Detect_ReturnsCorrectFormat() {
    var tempPath = Path.GetTempFileName();
    try {
      File.WriteAllBytes(tempPath, _MakeBam(2, 2));
      var fi = new FileInfo(tempPath);
      var format = ImageFormatDetector.Detect(fi);
      Assert.That(format, Is.EqualTo(ImageFormat.Bam));
    } finally {
      File.Delete(tempPath);
    }
  }

  [Test]
  public void ViewerStep_ImageFormatDetector_UnknownFile_ReturnsUnknown() {
    var tempPath = Path.GetTempFileName();
    try {
      File.WriteAllBytes(tempPath, new byte[] { 0xFF, 0xEE, 0xDD, 0xCC });
      var format = ImageFormatDetector.Detect(new FileInfo(tempPath));
      Assert.That(format, Is.EqualTo(ImageFormat.Unknown));
    } finally {
      File.Delete(tempPath);
    }
  }

  // ── Step 2: LoadRawImage ──────────────────────────────────────────────────

  [Test]
  public void ViewerStep_BitmapConverter_LoadRawImage_FromBam_Works() {
    var tempPath = Path.GetTempFileName();
    try {
      File.WriteAllBytes(tempPath, _MakeBam(10, 5));
      var raw = BitmapConverter.LoadRawImage(new FileInfo(tempPath), ImageFormat.Bam);
      Assert.That(raw, Is.Not.Null);
      Assert.That(raw!.Width, Is.EqualTo(10));
      Assert.That(raw.Height, Is.EqualTo(5));
      Assert.That(raw.Format, Is.EqualTo(PixelFormat.Rgba32));
      Assert.That(raw.PixelData.Length, Is.EqualTo(10 * 5 * 4));
    } finally {
      File.Delete(tempPath);
    }
  }

  [Test]
  public void ViewerStep_BitmapConverter_LoadRawImage_CorruptFile_ReturnsNull() {
    var tempPath = Path.GetTempFileName();
    try {
      // Valid BAM magic but truncated pixel data
      var bytes = _MakeBam(100, 100);
      var truncated = bytes.AsSpan(0, 20).ToArray(); // header + 4 bytes only
      File.WriteAllBytes(tempPath, truncated);
      var raw = BitmapConverter.LoadRawImage(new FileInfo(tempPath), ImageFormat.Bam);
      Assert.That(raw, Is.Null);
    } finally {
      File.Delete(tempPath);
    }
  }

  // ── Step 3: RawImage → Bitmap ────────────────────────────────────────────

  [Test]
  public void ViewerStep_BitmapConverter_RawImageToBitmap_Works() {
    var raw = new RawImage {
      Width = 4,
      Height = 3,
      Format = PixelFormat.Rgba32,
      PixelData = new byte[4 * 3 * 4],
    };
    for (var i = 0; i < raw.PixelData.Length; ++i)
      raw.PixelData[i] = (byte)i;

    var bmp = BitmapConverter.RawImageToBitmap(raw);
    Assert.That(bmp, Is.Not.Null);
    Assert.That(bmp.Width, Is.EqualTo(4));
    Assert.That(bmp.Height, Is.EqualTo(3));
    bmp.Dispose();
  }

  // ── Step 4: FormatRegistry entry lookup ──────────────────────────────────

  [Test]
  public void ViewerStep_FormatRegistry_GetEntry_ForBam_ReturnsEntry() {
    var entry = FormatRegistry.GetEntry(ImageFormat.Bam);
    Assert.That(entry, Is.Not.Null);
    Assert.That(entry!.Name, Is.EqualTo("Bam"));
    Assert.That(entry.PrimaryExtension, Is.EqualTo(".bam"));
  }

  [Test]
  public void ViewerStep_FormatRegistry_GetEntry_Bam_HasLoadRawImage() {
    var entry = FormatRegistry.GetEntry(ImageFormat.Bam);
    Assert.That(entry!.LoadRawImage, Is.Not.Null);
  }

  [Test]
  public void ViewerStep_FormatRegistry_GetEntry_Bam_HasConvertFromRawImage() {
    // BAM is writable — ConvertFromRawImage should be non-null
    var entry = FormatRegistry.GetEntry(ImageFormat.Bam);
    Assert.That(entry!.ConvertFromRawImage, Is.Not.Null);
  }

  // ── Step 5: Full end-to-end pipeline (the viewer's actual flow) ──────────

  [Test]
  public void ViewerStep_FullPipeline_DetectLoadConvert_Works() {
    // This mirrors exactly what Crush.Viewer's _LoadFile does
    var bytes = _MakeBam(8, 6);
    var tempPath = Path.GetTempFileName();
    try {
      File.WriteAllBytes(tempPath, bytes);
      var file = new FileInfo(tempPath);

      // Step 1: Detect
      var fmt = ImageFormatDetector.Detect(file);
      Assert.That(fmt, Is.EqualTo(ImageFormat.Bam));

      // Step 2: Load raw image
      var raw = BitmapConverter.LoadRawImage(file, fmt);
      Assert.That(raw, Is.Not.Null);

      // Step 3: Convert to bitmap
      var bmp = BitmapConverter.RawImageToBitmap(raw!);
      Assert.That(bmp, Is.Not.Null);
      Assert.That(bmp.Width, Is.EqualTo(8));
      Assert.That(bmp.Height, Is.EqualTo(6));

      // Step 4: Get format entry (for multi-image check)
      var entry = FormatRegistry.GetEntry(fmt);
      Assert.That(entry, Is.Not.Null);

      // Step 5: Check image count (single-image format)
      var count = entry?.GetImageCount?.Invoke(file) ?? 0;
      Assert.That(count, Is.EqualTo(0)); // BAM is not multi-image

      bmp.Dispose();
    } finally {
      File.Delete(tempPath);
    }
  }

  // ── Save-As path: ConversionTargets should include BAM ─────────────────

  [Test]
  public void ViewerStep_FormatRegistry_ConversionTargets_IncludesBam() {
    var targets = FormatRegistry.ConversionTargets.ToList();
    Assert.That(targets.Any(e => e.Format == ImageFormat.Bam), Is.True,
      "BAM should appear in the Save-As filter list");
  }

  [Test]
  public void ViewerStep_SaveAs_BamRoundTrip() {
    // Simulates Save-As: RawImage → bytes → file → reload
    var raw = new RawImage {
      Width = 3, Height = 2,
      Format = PixelFormat.Rgba32,
      PixelData = [
        255, 0, 0, 255,   0, 255, 0, 255,   0, 0, 255, 255,
        128, 128, 128, 255, 200, 100, 50, 128, 10, 20, 30, 200,
      ],
    };

    var entry = FormatRegistry.GetEntry(ImageFormat.Bam);
    Assert.That(entry!.ConvertFromRawImage, Is.Not.Null);
    var bytes = entry.ConvertFromRawImage!(raw);

    var tempPath = Path.GetTempFileName();
    try {
      File.WriteAllBytes(tempPath, bytes);
      var reloaded = BitmapConverter.LoadRawImage(new FileInfo(tempPath), ImageFormat.Bam);
      Assert.That(reloaded, Is.Not.Null);
      Assert.That(reloaded!.Width, Is.EqualTo(3));
      Assert.That(reloaded.Height, Is.EqualTo(2));
      Assert.That(reloaded.PixelData, Is.EqualTo(raw.PixelData));
    } finally {
      File.Delete(tempPath);
    }
  }
}
