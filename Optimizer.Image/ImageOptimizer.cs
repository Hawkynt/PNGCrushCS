using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.Intrinsics;
using System.Threading;
using System.Threading.Tasks;
using Crush.Core;
using FileFormat.Farbfeld;
using FileFormat.Qoi;
using Optimizer.Ani;
using Optimizer.Bmp;
using Optimizer.Cur;
using Optimizer.Gif;
using Optimizer.Ico;
using Optimizer.Jpeg;
using Optimizer.Pcx;
using Optimizer.Png;
using Optimizer.Tga;
using Optimizer.Tiff;
using Optimizer.WebP;

namespace Optimizer.Image;

/// <summary>Universal image optimizer that tries multiple formats to find the smallest output.</summary>
public sealed partial class ImageOptimizer {

  private readonly FileInfo _inputFile;
  private readonly ImageOptimizationOptions _options;
  private byte[]? _fileBytes;
  private ImageFormat _detectedFormat;
  private FileFormat.Core.RawImage? _cachedRawImage;

  public ImageOptimizer(FileInfo inputFile, ImageOptimizationOptions? options = null) {
    ArgumentNullException.ThrowIfNull(inputFile);
    if (!inputFile.Exists)
      throw new FileNotFoundException("Input file not found.", inputFile.FullName);

    _inputFile = inputFile;
    _options = options ?? new();
  }

  public async ValueTask<ImageOptimizationResult> OptimizeAsync(
    CancellationToken cancellationToken = default,
    IProgress<OptimizationProgress>? progress = null
  ) {
    var sw = Stopwatch.StartNew();
    var originalBytes = _fileBytes ??= await File.ReadAllBytesAsync(_inputFile.FullName, cancellationToken);
    if (_detectedFormat == default)
      _detectedFormat = ImageFormatDetector.Detect(originalBytes, _inputFile);
    var originalFormat = _detectedFormat;

    if (originalFormat == ImageFormat.Unknown)
      throw new NotSupportedException($"Unable to detect image format for: {_inputFile.Name}");

    var candidates = _BuildCandidates(originalFormat, originalBytes);
    var totalCandidates = candidates.Count;

    byte[]? bestResult = null;
    var bestFormat = originalFormat;
    var bestExtension = _GetExtension(originalFormat);
    var bestDetails = "Original (no improvement)";
    var bestSize = (long)originalBytes.Length;

    var combosCompleted = 0;

    for (var i = 0; i < candidates.Count; ++i) {
      cancellationToken.ThrowIfCancellationRequested();
      var candidate = candidates[i];

      var phaseProgress = new Progress<OptimizationProgress>(p => {
        progress?.Report(new(
          combosCompleted + p.CombosCompleted,
          totalCandidates,
          bestSize,
          $"{candidate.Format} ({i + 1}/{totalCandidates}): {p.Phase}"
        ));
      });

      try {
        var result = await candidate.Optimize(cancellationToken, phaseProgress);
        if (result != null && result.Length > 0 && result.Length < bestSize) {
          bestResult = result;
          bestSize = result.Length;
          bestFormat = candidate.Format;
          bestExtension = candidate.Extension;
          bestDetails = $"Best: {candidate.Format} ({FileFormatting.FormatFileSize(bestSize)})";
        }
      } catch (OperationCanceledException) {
        throw;
      } catch {
        // Skip failed candidates
      }

      ++combosCompleted;
      progress?.Report(new(combosCompleted, totalCandidates, bestSize, $"Done: {candidate.Format}"));
    }

    sw.Stop();

    return new(
      OriginalFormat: originalFormat,
      OutputFormat: bestFormat,
      OutputExtension: bestExtension,
      CompressedSize: bestSize,
      ProcessingTime: sw.Elapsed,
      FileContents: bestResult ?? originalBytes,
      Details: bestDetails
    );
  }

  private List<FormatCandidate> _BuildCandidates(ImageFormat originalFormat, byte[] originalBytes) {
    var candidates = new List<FormatCandidate>();
    var forceFormat = _options.ForceFormat;

    // Same-format optimization always comes first
    if (forceFormat == null || forceFormat == originalFormat)
      _AddSameFormatCandidate(candidates, originalFormat, originalBytes);

    if (forceFormat != null) {
      // Forced to a specific format
      if (forceFormat != originalFormat)
        _AddConversionCandidate(candidates, forceFormat.Value, originalBytes);

      return candidates;
    }

    if (!_options.AllowFormatConversion)
      return candidates;

    // Cross-format conversion only for raster images (not ICO/CUR/ANI)
    if (originalFormat is ImageFormat.Ico or ImageFormat.Cur or ImageFormat.Ani)
      return candidates;

    _AddRasterConversionCandidates(candidates, originalFormat, originalBytes);
    return candidates;
  }

  private void _AddSameFormatCandidate(List<FormatCandidate> candidates, ImageFormat format, byte[] originalBytes) {
    switch (format) {
      case ImageFormat.Png:
        candidates.Add(new(ImageFormat.Png, ".png", async (ct, p) => {
          using var bmp = _LoadBitmap();
          var opts = _options.PngOptions ?? new();
          var optimizer = new PngOptimizer(bmp, originalBytes, opts);
          var result = await optimizer.OptimizeAsync(ct, p);
          return result.FileContents;
        }));
        break;
      case ImageFormat.Gif:
        candidates.Add(new(ImageFormat.Gif, ".gif", async (ct, p) => {
          var opts = _options.GifOptions ?? new();
          var optimizer = GifOptimizer.FromFile(_inputFile, opts);
          var result = await optimizer.OptimizeAsync(ct, p);
          return result.FileContents;
        }));
        break;
      case ImageFormat.Tiff:
        candidates.Add(new(ImageFormat.Tiff, ".tiff", async (ct, p) => {
          var opts = _options.TiffOptions ?? new();
          var optimizer = TiffOptimizer.FromFile(_inputFile, opts);
          var result = await optimizer.OptimizeAsync(ct, p);
          return result.FileContents;
        }));
        break;
      case ImageFormat.Bmp:
        candidates.Add(new(ImageFormat.Bmp, ".bmp", async (ct, p) => {
          var opts = _options.BmpOptions ?? new();
          var optimizer = BmpOptimizer.FromFile(_inputFile, opts);
          var result = await optimizer.OptimizeAsync(ct, p);
          return result.FileContents;
        }));
        break;
      case ImageFormat.Tga:
        candidates.Add(new(ImageFormat.Tga, ".tga", async (ct, p) => {
          var opts = _options.TgaOptions ?? new();
          var optimizer = TgaOptimizer.FromFile(_inputFile, opts);
          var result = await optimizer.OptimizeAsync(ct, p);
          return result.FileContents;
        }));
        break;
      case ImageFormat.Pcx:
        candidates.Add(new(ImageFormat.Pcx, ".pcx", async (ct, p) => {
          var opts = _options.PcxOptions ?? new();
          var optimizer = PcxOptimizer.FromFile(_inputFile, opts);
          var result = await optimizer.OptimizeAsync(ct, p);
          return result.FileContents;
        }));
        break;
      case ImageFormat.Jpeg:
        candidates.Add(new(ImageFormat.Jpeg, ".jpg", async (ct, p) => {
          var opts = _options.JpegOptions ?? new();
          var optimizer = JpegOptimizer.FromFile(_inputFile, opts);
          var result = await optimizer.OptimizeAsync(ct, p);
          return result.FileContents;
        }));
        break;
      case ImageFormat.Ico:
        candidates.Add(new(ImageFormat.Ico, ".ico", async (ct, p) => {
          var opts = _options.IcoOptions ?? new();
          var optimizer = IcoOptimizer.FromFile(_inputFile, opts);
          var result = await optimizer.OptimizeAsync(ct, p);
          return result.FileContents;
        }));
        break;
      case ImageFormat.Cur:
        candidates.Add(new(ImageFormat.Cur, ".cur", async (ct, p) => {
          var opts = _options.CurOptions ?? new();
          var optimizer = CurOptimizer.FromFile(_inputFile, opts);
          var result = await optimizer.OptimizeAsync(ct, p);
          return result.FileContents;
        }));
        break;
      case ImageFormat.Ani:
        candidates.Add(new(ImageFormat.Ani, ".ani", async (ct, p) => {
          var opts = _options.AniOptions ?? new();
          var optimizer = AniOptimizer.FromFile(_inputFile, opts);
          var result = await optimizer.OptimizeAsync(ct, p);
          return result.FileContents;
        }));
        break;
      case ImageFormat.WebP:
        candidates.Add(new(ImageFormat.WebP, ".webp", async (ct, p) => {
          var opts = _options.WebPOptions ?? new();
          var optimizer = WebPOptimizer.FromFile(_inputFile, opts);
          var result = await optimizer.OptimizeAsync(ct, p);
          return result.FileContents;
        }));
        break;
    }
  }

  private void _AddConversionCandidate(List<FormatCandidate> candidates, ImageFormat targetFormat, byte[] originalBytes) {
    switch (targetFormat) {
      // Formats with dedicated optimizers
      case ImageFormat.Png:
        _AddPngConversion(candidates, originalBytes);
        break;
      case ImageFormat.Bmp:
        _AddBmpConversion(candidates);
        break;
      case ImageFormat.Tga:
        _AddTgaConversion(candidates);
        break;
      case ImageFormat.Pcx:
        _AddPcxConversion(candidates);
        break;
      case ImageFormat.Tiff:
        _AddTiffConversion(candidates);
        break;
      case ImageFormat.Gif:
        _AddGifConversion(candidates);
        break;
      case ImageFormat.Jpeg:
        _AddJpegConversion(candidates);
        break;
      // Custom pixel conversions
      case ImageFormat.Qoi:
        _AddQoiConversion(candidates);
        break;
      case ImageFormat.Farbfeld:
        _AddFarbfeldConversion(candidates);
        break;
      // All other registered formats — use FormatRegistry
      default:
        var entry = FormatRegistry.GetEntry(targetFormat);
        if (entry != null)
          _AddRawImageConversion(candidates, targetFormat, entry.PrimaryExtension, entry.ConvertFromRawImage);

        break;
    }
  }

  private void _AddRasterConversionCandidates(List<FormatCandidate> candidates, ImageFormat originalFormat, byte[] originalBytes) {
    var stats = _AnalyzeImage();

    // Formats with dedicated optimizers — best results via full optimization pipeline

    // PNG — always viable for raster
    if (originalFormat != ImageFormat.Png)
      _AddPngConversion(candidates, originalBytes);

    // BMP — no alpha support
    if (originalFormat != ImageFormat.Bmp && !stats.HasAlpha)
      _AddBmpConversion(candidates);

    // TGA — always viable
    if (originalFormat != ImageFormat.Tga)
      _AddTgaConversion(candidates);

    // PCX — no alpha support
    if (originalFormat != ImageFormat.Pcx && !stats.HasAlpha)
      _AddPcxConversion(candidates);

    // TIFF — always viable
    if (originalFormat != ImageFormat.Tiff)
      _AddTiffConversion(candidates);

    // GIF — only if ≤256 colors OR lossy
    if (originalFormat != ImageFormat.Gif && (stats.UniqueColors <= 256 || _options.AllowLossy))
      _AddGifConversion(candidates);

    // JPEG — only if lossy
    if (originalFormat != ImageFormat.Jpeg && _options.AllowLossy)
      _AddJpegConversion(candidates);

    // QOI — always viable for raster
    if (originalFormat != ImageFormat.Qoi)
      _AddQoiConversion(candidates);

    // Farbfeld — always viable for raster
    if (originalFormat != ImageFormat.Farbfeld)
      _AddFarbfeldConversion(candidates);

    // All other registered formats via FormatRegistry
    foreach (var entry in FormatRegistry.ConversionTargets) {
      if (entry.Format == originalFormat)
        continue;
      if ((entry.Capabilities & FormatCapability.MonochromeOnly) != 0 && stats.UniqueColors > 2)
        continue;
      if ((entry.Capabilities & FormatCapability.IndexedOnly) != 0 && stats.UniqueColors > 256)
        continue;

      _AddRawImageConversion(candidates, entry.Format, entry.PrimaryExtension, entry.ConvertFromRawImage);
    }
  }

  private void _AddPngConversion(List<FormatCandidate> candidates, byte[] originalBytes) {
    candidates.Add(new(ImageFormat.Png, ".png", async (ct, p) => {
      using var bmp = _LoadBitmap();
      var opts = _options.PngOptions ?? new();
      var optimizer = new PngOptimizer(bmp, opts);
      var result = await optimizer.OptimizeAsync(ct, p);
      return result.FileContents;
    }));
  }

  private void _AddBmpConversion(List<FormatCandidate> candidates) {
    candidates.Add(new(ImageFormat.Bmp, ".bmp", async (ct, p) => {
      using var bmp = _LoadBitmap();
      var tempFile = _SaveAsTempFormat(bmp, System.Drawing.Imaging.ImageFormat.Bmp, ".bmp");
      try {
        var opts = _options.BmpOptions ?? new();
        var optimizer = BmpOptimizer.FromFile(tempFile, opts);
        var result = await optimizer.OptimizeAsync(ct, p);
        return result.FileContents;
      } finally {
        try { tempFile.Delete(); } catch { /* best effort */ }
      }
    }));
  }

  private void _AddTgaConversion(List<FormatCandidate> candidates) {
    candidates.Add(new(ImageFormat.Tga, ".tga", async (ct, p) => {
      using var bmp = _LoadBitmap();
      var opts = _options.TgaOptions ?? new();
      var optimizer = new TgaOptimizer(bmp, opts);
      var result = await optimizer.OptimizeAsync(ct, p);
      return result.FileContents;
    }));
  }

  private void _AddPcxConversion(List<FormatCandidate> candidates) {
    candidates.Add(new(ImageFormat.Pcx, ".pcx", async (ct, p) => {
      using var bmp = _LoadBitmap();
      var opts = _options.PcxOptions ?? new();
      var optimizer = new PcxOptimizer(bmp, opts);
      var result = await optimizer.OptimizeAsync(ct, p);
      return result.FileContents;
    }));
  }

  private void _AddTiffConversion(List<FormatCandidate> candidates) {
    candidates.Add(new(ImageFormat.Tiff, ".tiff", async (ct, p) => {
      using var bmp = _LoadBitmap();
      var opts = _options.TiffOptions ?? new();
      var optimizer = new TiffOptimizer(bmp, opts);
      var result = await optimizer.OptimizeAsync(ct, p);
      return result.FileContents;
    }));
  }

  private void _AddGifConversion(List<FormatCandidate> candidates) {
    candidates.Add(new(ImageFormat.Gif, ".gif", async (ct, p) => {
      using var bmp = _LoadBitmap();
      var tempFile = _SaveAsTempFormat(bmp, System.Drawing.Imaging.ImageFormat.Gif, ".gif");
      try {
        var opts = _options.GifOptions ?? new();
        var optimizer = GifOptimizer.FromFile(tempFile, opts);
        var result = await optimizer.OptimizeAsync(ct, p);
        return result.FileContents;
      } finally {
        try { tempFile.Delete(); } catch { /* best effort */ }
      }
    }));
  }

  private void _AddJpegConversion(List<FormatCandidate> candidates) {
    candidates.Add(new(ImageFormat.Jpeg, ".jpg", async (ct, p) => {
      using var bmp = _LoadBitmap();
      var opts = _options.JpegOptions ?? new(AllowLossy: true);
      var optimizer = new JpegOptimizer(bmp, opts);
      var result = await optimizer.OptimizeAsync(ct, p);
      return result.FileContents;
    }));
  }

  private void _AddQoiConversion(List<FormatCandidate> candidates) {
    candidates.Add(new(ImageFormat.Qoi, ".qoi", (_, _) => {
      var raw = _LoadRawImageOrFromBitmap();
      var bgra = raw.ToBgra32();
      var totalPixels = raw.Width * raw.Height;
      var hasAlpha = _HasAlpha(bgra, totalPixels);

      byte[] pixelData;
      if (hasAlpha) {
        pixelData = new byte[totalPixels * 4];
        for (var i = 0; i < totalPixels; ++i) {
          var s = i * 4;
          pixelData[s] = bgra[s + 2];
          pixelData[s + 1] = bgra[s + 1];
          pixelData[s + 2] = bgra[s];
          pixelData[s + 3] = bgra[s + 3];
        }
      } else {
        pixelData = new byte[totalPixels * 3];
        for (var i = 0; i < totalPixels; ++i) {
          var s = i * 4;
          var d = i * 3;
          pixelData[d] = bgra[s + 2];
          pixelData[d + 1] = bgra[s + 1];
          pixelData[d + 2] = bgra[s];
        }
      }

      var file = new QoiFile {
        Width = raw.Width,
        Height = raw.Height,
        Channels = hasAlpha ? QoiChannels.Rgba : QoiChannels.Rgb,
        ColorSpace = QoiColorSpace.Srgb,
        PixelData = pixelData,
      };
      return ValueTask.FromResult<byte[]?>(QoiWriter.ToBytes(file));
    }));
  }

  private void _AddFarbfeldConversion(List<FormatCandidate> candidates) {
    candidates.Add(new(ImageFormat.Farbfeld, ".ff", (_, _) => {
      var raw = _LoadRawImageOrFromBitmap();
      var bgra = raw.ToBgra32();
      var totalPixels = raw.Width * raw.Height;
      var pixelData = new byte[totalPixels * 8];
      var idx = 0;

      for (var i = 0; i < totalPixels; ++i) {
        var s = i * 4;
        _WriteUInt16BigEndian(pixelData, ref idx, (ushort)(bgra[s + 2] * 257));
        _WriteUInt16BigEndian(pixelData, ref idx, (ushort)(bgra[s + 1] * 257));
        _WriteUInt16BigEndian(pixelData, ref idx, (ushort)(bgra[s] * 257));
        _WriteUInt16BigEndian(pixelData, ref idx, (ushort)(bgra[s + 3] * 257));
      }

      var file = new FarbfeldFile {
        Width = raw.Width,
        Height = raw.Height,
        PixelData = pixelData,
      };
      return ValueTask.FromResult<byte[]?>(FarbfeldWriter.ToBytes(file));
    }));
  }

  private void _AddRawImageConversion(
    List<FormatCandidate> candidates, ImageFormat format, string ext,
    Func<FileFormat.Core.RawImage, byte[]> convert
  ) {
    candidates.Add(new(format, ext, (_, _) => {
      var raw = _LoadRawImageOrFromBitmap();
      var bytes = convert(raw);
      return ValueTask.FromResult<byte[]?>(bytes);
    }));
  }

  private Bitmap _LoadBitmap()
    => _fileBytes != null
      ? BitmapConverter.LoadBitmap(_fileBytes, _detectedFormat)
      : BitmapConverter.LoadBitmap(_inputFile, _detectedFormat);

  private FileFormat.Core.RawImage? _LoadRawImage()
    => _cachedRawImage ??= _fileBytes != null
      ? BitmapConverter.LoadRawImage(_fileBytes, _detectedFormat)
      : BitmapConverter.LoadRawImage(_inputFile, _detectedFormat);

  private FileFormat.Core.RawImage _LoadRawImageOrFromBitmap() {
    var raw = _LoadRawImage();
    if (raw != null)
      return raw;

    using var bmp = _LoadBitmap();
    return _cachedRawImage = BitmapConverter.BitmapToRawImage(bmp);
  }

  private static FileInfo _SaveAsTempFormat(Bitmap bmp, System.Drawing.Imaging.ImageFormat format, string extension) {
    var tempPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}{extension}");
    bmp.Save(tempPath, format);
    return new FileInfo(tempPath);
  }

  private static void _WriteUInt16BigEndian(byte[] buffer, ref int offset, ushort value) {
    buffer[offset++] = (byte)(value >> 8);
    buffer[offset++] = (byte)(value & 0xFF);
  }

  /// <summary>Checks if any pixel in BGRA32 data has alpha &lt; 255. Uses SIMD Vector128 when available.</summary>
  internal static bool _HasAlpha(byte[] bgra, int totalPixels) {
    var byteCount = totalPixels * 4;
    var i = 0;

    if (Vector128.IsHardwareAccelerated && byteCount >= 16) {
      var nonAlphaMask = Vector128.Create(
        (byte)0xFF, 0xFF, 0xFF, 0x00,
        0xFF, 0xFF, 0xFF, 0x00,
        0xFF, 0xFF, 0xFF, 0x00,
        0xFF, 0xFF, 0xFF, 0x00
      );
      var allOnes = Vector128<byte>.AllBitsSet;

      for (; i + 16 <= byteCount; i += 16) {
        var v = Vector128.Create(bgra.AsSpan(i, 16));
        if (Vector128.BitwiseOr(v, nonAlphaMask) != allOnes)
          return true;
      }
    }

    for (; i + 3 < byteCount; i += 4)
      if (bgra[i + 3] < 255)
        return true;

    return false;
  }

  private ImageStats _AnalyzeImage() {
    var raw = _LoadRawImageOrFromBitmap();
    var bgra = raw.ToBgra32();
    var totalPixels = raw.Width * raw.Height;
    var uniqueColors = new HashSet<int>();
    var hasAlpha = false;

    for (var i = 0; i < totalPixels; ++i) {
      var o = i * 4;
      var b = bgra[o];
      var g = bgra[o + 1];
      var r = bgra[o + 2];
      var a = bgra[o + 3];
      uniqueColors.Add((r << 16) | (g << 8) | b);
      if (a < 255)
        hasAlpha = true;
    }

    return new(uniqueColors.Count, hasAlpha);
  }

  private static string _GetExtension(ImageFormat format) => format switch {
    // Formats not in FormatRegistry (no IImageFileFormat implementation)
    ImageFormat.Gif => ".gif",
    ImageFormat.Ani => ".ani",
    ImageFormat.WebP => ".webp",
    ImageFormat.ScitexCt => ".sct",
    ImageFormat.UtahRle => ".rle",
    ImageFormat.Wad => ".wad",
    _ => FormatRegistry.GetExtension(format),
  };
}
