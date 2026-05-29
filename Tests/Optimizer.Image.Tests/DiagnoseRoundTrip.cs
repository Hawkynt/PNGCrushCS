using System;
using System.IO;
using FileFormat.Core;
using Optimizer.Image;

namespace Optimizer.Image.Tests;

[TestFixture]
public sealed class DiagnoseRoundTrip {

  [Test]
  public void DiagnoseWebP() => _Diagnose(ImageFormat.WebP);

  [Test]
  public void DiagnoseXbm() => _Diagnose(ImageFormat.Xbm);

  [Test]
  public void DiagnoseYuvRaw() => _Diagnose(ImageFormat.YuvRaw);

  [Test]
  public void DiagnoseSeattleFilmWorks() => _Diagnose(ImageFormat.SeattleFilmWorks);

  [Test]
  public void DiagnoseCcitt() => _Diagnose(ImageFormat.Ccitt);

  [Test]
  public void DiagnoseEnviDirect() {
    var raw = _MakeGray8(32, 32);
    var file = FileFormat.Envi.EnviFile.FromRawImage(raw);
    var bytes = FileFormat.Envi.EnviWriter.ToBytes(file);
    Console.WriteLine($"file.PixelData first 20: {string.Join(",", file.PixelData[..20])}");
    Console.WriteLine($"Bytes first 110: {BitConverter.ToString(bytes, 0, Math.Min(110, bytes.Length))}");
    var read = FileFormat.Envi.EnviReader.FromBytes(bytes);
    Console.WriteLine($"Read W={read.Width} H={read.Height} Bands={read.Bands} DataType={read.DataType} Interleave={read.Interleave}");
    Console.WriteLine($"Read pixelData len: {read.PixelData.Length}");
    Console.WriteLine($"Read pixelData first 20: {string.Join(",", read.PixelData[..20])}");
    var rawOut = FileFormat.Envi.EnviFile.ToRawImage(read);
    Console.WriteLine($"RawOut Format: {rawOut.Format}, PixelData first 20: {string.Join(",", rawOut.PixelData[..20])}");
  }

  [Test]
  public void DiagnoseEnvi() {
    var entry = FormatRegistry.GetEntry(ImageFormat.Envi)!;
    var raw = _MakeGray8(32, 32);
    var bytes = entry.ConvertFromRawImage!(raw);
    Console.WriteLine($"Saved {bytes.Length} bytes");
    var tempFile = Path.Combine(Path.GetTempPath(), $"diag_{Guid.NewGuid():N}.hdr");
    try {
      File.WriteAllBytes(tempFile, bytes);
      var loaded = entry.LoadRawImage!(new FileInfo(tempFile));
      Console.WriteLine($"Loaded W={loaded?.Width} H={loaded?.Height} fmt={loaded?.Format} len={loaded?.PixelData?.Length}");
      if (loaded?.PixelData != null) {
        Console.WriteLine($"Expected first 20: {string.Join(",", raw.PixelData[..20])}");
        Console.WriteLine($"Loaded first 20:   {string.Join(",", loaded.PixelData[..20])}");
        // Compare byte-by-byte for first mismatch
        for (var i = 0; i < Math.Min(loaded.PixelData.Length, raw.PixelData.Length); ++i) {
          if (loaded.PixelData[i] != raw.PixelData[i]) {
            Console.WriteLine($"First mismatch at index {i}: expected {raw.PixelData[i]} got {loaded.PixelData[i]}");
            break;
          }
        }
        // Show raw bytes around the header boundary
        Console.WriteLine($"Bytes 100-130: {BitConverter.ToString(bytes, 100, Math.Min(30, bytes.Length - 100))}");
      }
    } finally { try { File.Delete(tempFile); } catch { } }
  }

  private static void _Diagnose(ImageFormat format) {
    var entry = FormatRegistry.GetEntry(format)!;

    var w = 32; var h = 32;
    if (entry.AllowedDimensions is { Length: > 0 } dims) {
      w = dims[0].Width.SnapToValid(32);
      h = dims[0].Height.SnapToValid(32);
    }

    // Try multiple input formats
    foreach (var label in new[] { "Indexed1", "Indexed8", "Gray8", "Rgb24", "Rgba32" }) {
      var raw = label switch {
        "Indexed1" => _MakeIndexed1(w, h),
        "Indexed8" => _MakeIndexed8(w, h),
        "Gray8" => _MakeGray8(w, h),
        "Rgb24" => _MakeRgb24(w, h),
        "Rgba32" => _MakeRgba32(w, h),
        _ => throw new Exception()
      };

      byte[]? bytes = null;
      try {
        bytes = entry.ConvertFromRawImage!(raw);
      } catch (Exception ex) {
        Console.WriteLine($"[{format}/{label}] WRITE failed: {ex.GetType().Name}: {ex.Message}");
        continue;
      }
      if (bytes == null || bytes.Length == 0) {
        Console.WriteLine($"[{format}/{label}] writer returned empty bytes");
        continue;
      }

      var tempFile = Path.Combine(Path.GetTempPath(), $"diag_{Guid.NewGuid():N}{entry.PrimaryExtension}");
      try {
        File.WriteAllBytes(tempFile, bytes);
        var loaded = entry.LoadRawImage!(new FileInfo(tempFile));
        if (loaded == null) {
          // Try direct read with stack trace
          try {
            var fileBytes = File.ReadAllBytes(tempFile);
            var readers = entry.LoadRawImageFromBytes;
            if (readers != null) {
              try { _ = readers(fileBytes); }
              catch (Exception ex3) {
                Console.WriteLine($"[{format}/{label}] LoadRawImageFromBytes threw: {ex3.GetType().Name}: {ex3.Message}");
                Console.WriteLine(ex3.StackTrace);
                return;
              }
            }
          } catch { }
          Console.WriteLine($"[{format}/{label}] WRITE ok ({bytes.Length} bytes) but READ returned null");
        } else {
          Console.WriteLine($"[{format}/{label}] OK: written {bytes.Length} bytes, decoded {loaded.Width}x{loaded.Height} {loaded.Format}");
          return;
        }
      } finally {
        try { File.Delete(tempFile); } catch { }
      }
    }
  }

  private static RawImage _MakeIndexed1(int w, int h) {
    var stride = (w + 7) / 8;
    var pixels = new byte[stride * h];
    for (var y = 0; y < h; ++y)
      for (var x = 0; x < w; ++x)
        if (((x + y) & 1) != 0)
          pixels[y * stride + (x >> 3)] |= (byte)(1 << (7 - (x & 7)));
    return new() { Width = w, Height = h, Format = PixelFormat.Indexed1, PixelData = pixels, Palette = [0, 0, 0, 255, 255, 255], PaletteCount = 2 };
  }

  private static RawImage _MakeIndexed8(int w, int h) {
    var pixels = new byte[w * h];
    for (var i = 0; i < pixels.Length; ++i) pixels[i] = (byte)(i % 4);
    return new() { Width = w, Height = h, Format = PixelFormat.Indexed8, PixelData = pixels, Palette = [0, 0, 0, 85, 85, 85, 170, 170, 170, 255, 255, 255], PaletteCount = 4 };
  }

  private static RawImage _MakeGray8(int w, int h) {
    var pixels = new byte[w * h];
    for (var y = 0; y < h; ++y)
      for (var x = 0; x < w; ++x)
        pixels[y * w + x] = (byte)((x + y) * 255 / Math.Max(1, w + h - 2));
    return new() { Width = w, Height = h, Format = PixelFormat.Gray8, PixelData = pixels };
  }

  private static RawImage _MakeRgb24(int w, int h) {
    var pixels = new byte[w * h * 3];
    for (var y = 0; y < h; ++y)
      for (var x = 0; x < w; ++x) {
        var i = (y * w + x) * 3;
        pixels[i] = (byte)((x + y) * 255 / Math.Max(1, w + h - 2));
        pixels[i + 1] = (byte)(y * 255 / Math.Max(1, h - 1));
        pixels[i + 2] = (byte)(x * 255 / Math.Max(1, w - 1));
      }
    return new() { Width = w, Height = h, Format = PixelFormat.Rgb24, PixelData = pixels };
  }

  private static RawImage _MakeRgba32(int w, int h) {
    var pixels = new byte[w * h * 4];
    for (var y = 0; y < h; ++y)
      for (var x = 0; x < w; ++x) {
        var i = (y * w + x) * 4;
        pixels[i] = (byte)((x + y) * 255 / Math.Max(1, w + h - 2));
        pixels[i + 1] = (byte)(y * 255 / Math.Max(1, h - 1));
        pixels[i + 2] = (byte)(x * 255 / Math.Max(1, w - 1));
        pixels[i + 3] = 255;
      }
    return new() { Width = w, Height = h, Format = PixelFormat.Rgba32, PixelData = pixels };
  }
}
