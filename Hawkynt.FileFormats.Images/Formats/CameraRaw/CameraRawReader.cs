using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Collections.Generic;
using System.Text;
using FileFormat.Jpeg;
using FileFormat.Core;

namespace FileFormat.CameraRaw;

/// <summary>Reads Camera RAW files (CR2/NEF/ARW/ORF/RW2/PEF/RAF) from bytes, streams, or file paths.
/// Extracts the largest preview or demosaiced CFA image from the TIFF IFD chain.
/// Supports compressed CFA data: lossless JPEG (compression 7/34892), Nikon NEF (34713), Sony ARW (32767), Deflate (8).</summary>
public static class CameraRawReader {

  /// <summary>Minimum valid file size: 8-byte TIFF header + 2-byte IFD count + 4-byte next IFD offset.</summary>
  private const int _MIN_TIFF_SIZE = 14;

  // Compression tag values
  private const int _COMPRESSION_UNCOMPRESSED = 1;
  private const int _COMPRESSION_JPEG = 7;
  private const int _COMPRESSION_DEFLATE = 8;
  private const int _COMPRESSION_NIKON_NEF = 34713;
  private const int _COMPRESSION_SONY_ARW = 32767;
  private const int _COMPRESSION_DNG_LOSSLESS_JPEG = 34892;

  /// <summary>Set of compression values that we can decode for CFA data.</summary>
  private static readonly int[] _SUPPORTED_CFA_COMPRESSIONS = [
    _COMPRESSION_UNCOMPRESSED,
    _COMPRESSION_JPEG,
    _COMPRESSION_DEFLATE,
    _COMPRESSION_NIKON_NEF,
    _COMPRESSION_SONY_ARW,
    _COMPRESSION_DNG_LOSSLESS_JPEG,
  ];

  /// <summary>RAF header signature.</summary>
  private static readonly byte[] _RAF_SIGNATURE = Encoding.ASCII.GetBytes("FUJIFILMCCD-RAW");

  public static CameraRawFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Camera RAW file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static CameraRawFile FromStream(Stream stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.CanSeek) {
      var data = new byte[stream.Length - stream.Position];
      stream.ReadExactly(data);
      return FromBytes(data);
    }
    using var ms = new MemoryStream();
    stream.CopyTo(ms);
    return FromBytes(ms.ToArray());
  }

  public static CameraRawFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < _MIN_TIFF_SIZE)
      throw new InvalidDataException("Data too small for a valid Camera RAW file.");

    // Internal APIs require byte[], so materialize once
    var bytes = data.ToArray();

    // Check for RAF signature first
    if (_IsRafSignature(bytes))
      return _ParseRaf(bytes);

    // Try TIFF-based parsing
    return _ParseTiffBased(bytes);
  }

  public static CameraRawFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }

  private static bool _IsRafSignature(byte[] data) {
    if (data.Length < _RAF_SIGNATURE.Length)
      return false;

    for (var i = 0; i < _RAF_SIGNATURE.Length; ++i)
      if (data[i] != _RAF_SIGNATURE[i])
        return false;

    return true;
  }

  private static CameraRawFile _ParseRaf(byte[] data) {
    if (data.Length < 108)
      throw new InvalidDataException("RAF file too small to contain offset table.");

    var jpegOffset = (int)RawTiffParser.ReadUInt32(data, 84, false);
    var jpegLength = (int)RawTiffParser.ReadUInt32(data, 88, false);

    if (jpegOffset > 0 && jpegOffset + 8 < data.Length) {
      var embedded = data[jpegOffset];
      var embedded2 = data[jpegOffset + 1];
      if ((embedded == (byte)'I' && embedded2 == (byte)'I') || (embedded == (byte)'M' && embedded2 == (byte)'M')) {
        var tiffData = new byte[data.Length - jpegOffset];
        data.AsSpan(jpegOffset, tiffData.Length).CopyTo(tiffData.AsSpan(0));
        var result = _ParseTiffBased(tiffData);
        return new() {
          Width = result.Width,
          Height = result.Height,
          PixelData = result.PixelData,
          Manufacturer = CameraRawManufacturer.Fujifilm,
          Model = result.Model,
        };
      }
    }

    // The offset table may point at a JPEG rather than a TIFF, and ours does. Only the TIFF case was
    // handled, so the reader fell through to returning a picture nought wide and nought tall — which
    // is not a refusal, and was caught downstream only because a picture must hold its own pixels.
    if (jpegOffset > 0 && jpegLength > 0 && jpegOffset + jpegLength <= data.Length
        && data[jpegOffset] == 0xFF && data[jpegOffset + 1] == 0xD8) {
      var preview = JpegFile.ToRawImage(JpegReader.FromSpan(data.AsSpan(jpegOffset, jpegLength)));
      var rgb = PixelConverter.Convert(preview, PixelFormat.Rgb24);
      return new() {
        Width = rgb.Width,
        Height = rgb.Height,
        PixelData = rgb.PixelData,
        Manufacturer = CameraRawManufacturer.Fujifilm,
        Model = "Fujifilm",
      };
    }

    throw new InvalidDataException("A Fujifilm raw file states neither a TIFF nor a JPEG at the offset it names.");
  }

  private static CameraRawFile _ParseTiffBased(byte[] data) {
    var isLittleEndian = RawTiffParser.DetectByteOrder(data);
    RawTiffParser.ValidateMagic(data, isLittleEndian);
    var firstIfdOffset = RawTiffParser.ReadFirstIfdOffset(data, isLittleEndian);

    var (images, make, model, hasDngVersion) = RawTiffParser.ParseAllIfds(data, firstIfdOffset, isLittleEndian);
    var manufacturer = RawTiffParser.IdentifyManufacturer(make, data);

    // Try CFA demosaicing path: look for an IFD with CFA pattern data and strip data
    // Support both uncompressed (1) and compressed CFA data
    var cfaImage = images
      .Where(img => img.HasCfa && _SUPPORTED_CFA_COMPRESSIONS.Contains(img.Compression) && img.StripOffsets is { Length: > 0 } && img.StripByteCounts is { Length: > 0 })
      .OrderByDescending(img => (long)img.Width * img.Height)
      .FirstOrDefault();

    if (cfaImage != null) {
      var result = _DemosaicCfa(data, cfaImage, isLittleEndian, manufacturer);
      if (result != null)
        return new() {
          Width = cfaImage.Width,
          Height = cfaImage.Height,
          PixelData = result,
          Manufacturer = manufacturer,
          Model = model,
        };
    }

    // Fall back to finding the largest uncompressed preview image (compression == 1) with strip data
    var bestImage = images
      .Where(img => img.Compression == 1 && img.StripOffsets is { Length: > 0 } && img.StripByteCounts is { Length: > 0 })
      .OrderByDescending(img => (long)img.Width * img.Height)
      .FirstOrDefault();

    if (bestImage == null) {
      // Most raw files carry no uncompressed picture at all: their previews are JPEG, and that is
      // what every viewer shows when the sensor data is in a compression it cannot undo. Refusing
      // the file outright throws away a picture the file plainly contains.
      var preview = _LargestJpegPreview(data, images, isLittleEndian);
      if (preview != null)
        return new() {
          Width = preview.Width,
          Height = preview.Height,
          PixelData = preview.PixelData,
          Manufacturer = manufacturer,
          Model = model,
        };

      throw new InvalidDataException("No uncompressed image and no JPEG preview in Camera RAW file IFD chain.");
    }

    var bytesPerPixel = bestImage.SamplesPerPixel * (bestImage.BitsPerSample / 8);
    var totalPixelBytes = bestImage.Width * bestImage.Height * bytesPerPixel;
    var pixelData = RawTiffParser.ExtractStripData(data, bestImage.StripOffsets!, bestImage.StripByteCounts!, totalPixelBytes);

    if (bestImage.SamplesPerPixel != 3 || bestImage.BitsPerSample != 8)
      pixelData = _ConvertToRgb24(pixelData, bestImage.Width, bestImage.Height, bestImage.SamplesPerPixel, bestImage.BitsPerSample);

    return new() {
      Width = bestImage.Width,
      Height = bestImage.Height,
      PixelData = pixelData,
      Manufacturer = manufacturer,
      Model = model,
    };
  }

  /// <summary>The largest JPEG the file carries, decoded, or null when it carries none.</summary>
  /// <remarks>
  /// A raw states its preview in one of two ways: the old pair of tags that give an offset and a
  /// length outright, or a strip whose compression says JPEG. Both are tried, and the largest of
  /// what turns up wins — a file often holds a thumbnail as well as a full-size preview, and the
  /// thumbnail is not what anybody wants to see.
  /// </remarks>
  private static RawImage? _LargestJpegPreview(
    byte[] data, IEnumerable<RawTiffParser.IfdImage> images, bool isLittleEndian) {
    RawImage? best = null;

    foreach (var image in images) {
      foreach (var (offset, length) in _JpegRanges(image)) {
        if (offset <= 0 || length <= 0 || offset + length > data.Length)
          continue;

        // A JPEG starts with the start-of-image marker; anything else is a tag pointing elsewhere.
        if (data[offset] != 0xFF || data[offset + 1] != 0xD8)
          continue;

        RawImage decoded;
        try {
          decoded = JpegFile.ToRawImage(JpegReader.FromSpan(data.AsSpan(offset, length)));
        } catch (Exception) {
          // A preview that will not decode is not a reason to fail the file; another may.
          continue;
        }

        if (best == null || (long)decoded.Width * decoded.Height > (long)best.Width * best.Height)
          best = decoded;
      }
    }

    best ??= _LargestJpegInTheFile(data);

    return best == null ? null : PixelConverter.Convert(best, PixelFormat.Rgb24);
  }

  /// <summary>How many start-of-image markers are worth trying before giving up.</summary>
  /// <remarks>
  /// A raw file is megabytes of sensor data and the byte pair that starts a JPEG turns up in it by
  /// chance, so this stops after a bounded number rather than attempting a decode at every hit. A
  /// dozen proved too few: one file here carries a hundred and fifty kilobytes of sensor data before
  /// its preview, and the chance hits in that ran the count out before the picture was reached.
  /// </remarks>
  private const int _JPEG_CANDIDATE_LIMIT = 96;

  /// <summary>
  /// The largest JPEG anywhere in the file, when the directories name none.
  /// </summary>
  /// <remarks>
  /// Panasonic, Pentax and Kodak each put their preview behind a tag of their own that the directory
  /// walk here does not know, so a file plainly holding a picture reported holding none. Rather than
  /// learn each maker's tag one at a time, this looks for the picture itself: every start-of-image
  /// marker is a candidate, whichever decodes largest wins, and one that does not decode costs
  /// nothing.
  /// </remarks>
  private static RawImage? _LargestJpegInTheFile(byte[] data) {
    RawImage? best = null;
    var tried = 0;

    for (var at = 0; at + 3 < data.Length && tried < _JPEG_CANDIDATE_LIMIT; ++at) {
      if (data[at] != 0xFF || data[at + 1] != 0xD8 || data[at + 2] != 0xFF)
        continue;

      ++tried;
      try {
        var decoded = JpegFile.ToRawImage(JpegReader.FromSpan(data.AsSpan(at)));
        if (best == null || (long)decoded.Width * decoded.Height > (long)best.Width * best.Height)
          best = decoded;
      } catch (Exception) {
        // Two bytes that merely look like a marker; the next candidate may be the picture.
      }
    }

    return best;
  }

  /// <summary>Where an IFD says its JPEG data is, by either of the two conventions.</summary>
  private static IEnumerable<(int Offset, int Length)> _JpegRanges(RawTiffParser.IfdImage image) {
    if (image.JpegOffset > 0 && image.JpegLength > 0)
      yield return ((int)image.JpegOffset, (int)image.JpegLength);

    // Compression 6 is the old JPEG tag and 7 the one that replaced it; both mean the strips hold
    // a JPEG stream rather than samples.
    if (image.Compression is not (6 or 7) || image.StripOffsets is not { Length: > 0 } offsets
        || image.StripByteCounts is not { Length: > 0 } counts)
      yield break;

    for (var i = 0; i < offsets.Length && i < counts.Length; ++i)
      yield return ((int)offsets[i], (int)counts[i]);
  }

  /// <summary>Demosaic CFA raw sensor data from an IFD with Bayer pattern information. Supports compressed CFA data.</summary>
  /// <returns>RGB24 pixel data, or null if demosaicing fails.</returns>
  private static byte[]? _DemosaicCfa(byte[] data, RawTiffParser.IfdImage ifd, bool isLittleEndian, CameraRawManufacturer manufacturer) {
    var pattern = _DecodeBayerPattern(ifd.CfaPattern!);
    if (pattern == null)
      return null;

    var width = ifd.Width;
    var height = ifd.Height;
    var pixelCount = width * height;

    var blackLevel = ifd.BlackLevel ?? [0];
    var whiteLevel = ifd.WhiteLevel > 0 ? ifd.WhiteLevel : (1 << ifd.BitsPerSample) - 1;

    float[]? whiteBalance = null;
    if (ifd.AsShotNeutral is { Length: >= 3 }) {
      var neutral = ifd.AsShotNeutral;
      if (neutral[0] > 0 && neutral[1] > 0 && neutral[2] > 0) {
        var gNeutral = neutral[1];
        whiteBalance = [
          gNeutral / neutral[0],
          1.0f,
          gNeutral / neutral[2]
        ];
      }
    }

    // Decompress CFA data based on compression type
    ushort[]? rawUInt16 = null;
    byte[]? rawBytes = null;

    switch (ifd.Compression) {
      case _COMPRESSION_UNCOMPRESSED:
        if (ifd.BitsPerSample > 8)
          rawUInt16 = RawTiffParser.ExtractStripDataUInt16(data, ifd.StripOffsets!, ifd.StripByteCounts!, pixelCount, ifd.BitsPerSample, isLittleEndian);
        else
          rawBytes = RawTiffParser.ExtractStripData(data, ifd.StripOffsets!, ifd.StripByteCounts!, pixelCount);
        break;

      case _COMPRESSION_JPEG:
      case _COMPRESSION_DNG_LOSSLESS_JPEG:
        rawUInt16 = _DecodeLosslessJpegCfa(data, ifd, manufacturer);
        break;

      case _COMPRESSION_NIKON_NEF:
        rawUInt16 = _DecodeNikonCfa(data, ifd);
        break;

      case _COMPRESSION_SONY_ARW:
        rawUInt16 = _DecodeSonyCfa(data, ifd);
        break;

      case _COMPRESSION_DEFLATE:
        rawUInt16 = _DecodeDeflateCfa(data, ifd, isLittleEndian);
        break;

      default:
        return null;
    }

    // Preprocess and demosaic
    byte[] rgb;
    if (rawUInt16 != null) {
      var preprocessed = RawPreprocessor.Process(rawUInt16, width, height, pattern.Value, blackLevel, whiteLevel, whiteBalance);
      rgb = BayerDemosaic.Ahd(preprocessed, width, height, pattern.Value);
    } else if (rawBytes != null) {
      var preprocessed = RawPreprocessor.Process(rawBytes, width, height, pattern.Value, blackLevel, whiteLevel, whiteBalance);
      rgb = BayerDemosaic.Ahd(preprocessed, width, height, pattern.Value);
    } else
      return null;

    // Post-process: color matrix + sRGB gamma
    float[]? colorMatrix = null;
    if (ifd.ColorMatrix1 is { Length: >= 9 })
      colorMatrix = _CameraToSrgbMatrix(ifd.ColorMatrix1);

    RawPostprocessor.Process(rgb, colorMatrix);

    return rgb;
  }

  /// <summary>Decode lossless JPEG compressed CFA data (compression 7 or 34892).</summary>
  private static ushort[]? _DecodeLosslessJpegCfa(byte[] data, RawTiffParser.IfdImage ifd, CameraRawManufacturer manufacturer) {
    // Extract the compressed strip data as raw bytes
    var totalStripBytes = 0;
    for (var i = 0; i < ifd.StripByteCounts!.Length; ++i)
      totalStripBytes += (int)ifd.StripByteCounts[i];

    var jpegData = RawTiffParser.ExtractStripData(data, ifd.StripOffsets!, ifd.StripByteCounts!, totalStripBytes);

    try {
      var decoded = LosslessJpegDecoder.Decode(jpegData);

      // For Canon CR2 with slice info, reassemble slices
      if (manufacturer == CameraRawManufacturer.Canon && ifd.SliceInfo is { Length: >= 3 })
        return LosslessJpegDecoder.ReassembleCanonSlices(decoded.Samples, ifd.Width, ifd.Height, ifd.SliceInfo, decoded.ComponentCount);

      // For multi-component data, de-interleave to single channel CFA
      if (decoded.ComponentCount > 1) {
        var result = new ushort[ifd.Width * ifd.Height];
        var srcIdx = 0;
        for (var i = 0; i < result.Length && srcIdx < decoded.Samples.Length; ++i) {
          result[i] = decoded.Samples[srcIdx];
          srcIdx += decoded.ComponentCount;
        }

        return result;
      }

      // Single component: samples are directly the CFA data
      if (decoded.Samples.Length >= ifd.Width * ifd.Height)
        return decoded.Samples;

      // Pad if needed
      var output = new ushort[ifd.Width * ifd.Height];
      Array.Copy(decoded.Samples, output, Math.Min(decoded.Samples.Length, output.Length));
      return output;
    } catch (InvalidDataException) {
      return null;
    }
  }

  /// <summary>Decode Nikon NEF compressed CFA data (compression 34713).</summary>
  private static ushort[]? _DecodeNikonCfa(byte[] data, RawTiffParser.IfdImage ifd) {
    if (ifd.StripOffsets is not { Length: > 0 } || ifd.StripByteCounts is not { Length: > 0 })
      return null;

    var stripOffset = (int)ifd.StripOffsets[0];
    var stripLength = (int)ifd.StripByteCounts[0];

    try {
      return NikonDecompressor.Decompress(data, stripOffset, stripLength, ifd.Width, ifd.Height, ifd.BitsPerSample, null, false);
    } catch (InvalidDataException) {
      return null;
    }
  }

  /// <summary>Decode Sony ARW compressed CFA data (compression 32767).</summary>
  private static ushort[]? _DecodeSonyCfa(byte[] data, RawTiffParser.IfdImage ifd) {
    if (ifd.StripOffsets is not { Length: > 0 } || ifd.StripByteCounts is not { Length: > 0 })
      return null;

    var stripOffset = (int)ifd.StripOffsets[0];
    var stripLength = (int)ifd.StripByteCounts[0];

    try {
      return SonyDecompressor.Decompress(data, stripOffset, stripLength, ifd.Width, ifd.Height, ifd.BitsPerSample);
    } catch (InvalidDataException) {
      return null;
    }
  }

  /// <summary>Decode Deflate (zlib) compressed CFA data (compression 8).</summary>
  private static ushort[]? _DecodeDeflateCfa(byte[] data, RawTiffParser.IfdImage ifd, bool isLittleEndian) {
    if (ifd.StripOffsets is not { Length: > 0 } || ifd.StripByteCounts is not { Length: > 0 })
      return null;

    try {
      // Collect all strip data
      var totalStripBytes = 0;
      for (var i = 0; i < ifd.StripByteCounts.Length; ++i)
        totalStripBytes += (int)ifd.StripByteCounts[i];

      var compressedData = RawTiffParser.ExtractStripData(data, ifd.StripOffsets, ifd.StripByteCounts, totalStripBytes);

      // Decompress with zlib (DeflateStream handles raw deflate; zlib has a 2-byte header we need to skip)
      byte[] decompressed;
      using (var compressedStream = new MemoryStream(compressedData)) {
        // Try zlib format first (skip 2-byte zlib header)
        if (compressedData.Length >= 2 && (compressedData[0] & 0x0F) == 8) {
          compressedStream.Position = 2; // skip zlib header
        }

        using var deflateStream = new DeflateStream(compressedStream, CompressionMode.Decompress);
        using var outputStream = new MemoryStream();
        deflateStream.CopyTo(outputStream);
        decompressed = outputStream.ToArray();
      }

      // Convert decompressed bytes to ushort array
      var pixelCount = ifd.Width * ifd.Height;
      var result = new ushort[pixelCount];

      if (ifd.BitsPerSample <= 8) {
        for (var i = 0; i < pixelCount && i < decompressed.Length; ++i)
          result[i] = decompressed[i];
      } else {
        for (var i = 0; i < pixelCount; ++i) {
          var off = i * 2;
          if (off + 2 > decompressed.Length)
            break;
          result[i] = isLittleEndian
            ? (ushort)(decompressed[off] | (decompressed[off + 1] << 8))
            : (ushort)((decompressed[off] << 8) | decompressed[off + 1]);
        }
      }

      return result;
    } catch {
      return null;
    }
  }

  /// <summary>Decode a 2x2 CFA pattern byte array into a BayerPattern enum.</summary>
  private static BayerPattern? _DecodeBayerPattern(byte[] cfaPattern) {
    if (cfaPattern.Length < 4)
      return null;

    // CFA pattern values: 0=Red, 1=Green, 2=Blue
    var p0 = cfaPattern[0];
    var p1 = cfaPattern[1];
    var p2 = cfaPattern[2];
    var p3 = cfaPattern[3];

    return (p0, p1, p2, p3) switch {
      (0, 1, 1, 2) => BayerPattern.RGGB,
      (2, 1, 1, 0) => BayerPattern.BGGR,
      (1, 0, 2, 1) => BayerPattern.GRBG,
      (1, 2, 0, 1) => BayerPattern.GBRG,
      _ => null
    };
  }

  /// <summary>Compute a camera-to-sRGB matrix from the camera-to-XYZ D65 ColorMatrix1.</summary>
  private static float[] _CameraToSrgbMatrix(float[] cameraToXyz) {
    // sRGB D65 XYZ-to-linear-sRGB matrix (IEC 61966-2-1)
    ReadOnlySpan<float> xyzToSrgb =
    [
      3.2404542f, -1.5371385f, -0.4985314f,
      -0.9692660f, 1.8760108f, 0.0415560f,
      0.0556434f, -0.2040259f, 1.0572252f
    ];

    // Multiply: sRGB = xyzToSrgb * cameraToXyz (both 3x3 row-major)
    var result = new float[9];
    for (var row = 0; row < 3; ++row)
      for (var col = 0; col < 3; ++col) {
        var sum = 0f;
        for (var k = 0; k < 3; ++k)
          sum += xyzToSrgb[row * 3 + k] * cameraToXyz[k * 3 + col];
        result[row * 3 + col] = sum;
      }

    return result;
  }

  private static byte[] _ConvertToRgb24(byte[] sourceData, int width, int height, int samplesPerPixel, int bitsPerSample) {
    var totalPixels = width * height;

    if (samplesPerPixel == 1 && bitsPerSample == 8)
      return PixelConverter.Gray8ToRgb24(sourceData, totalPixels);

    if (samplesPerPixel == 4 && bitsPerSample == 8)
      return PixelConverter.Rgba32ToRgb24(sourceData, totalPixels);

    // Best-effort: copy what we have as RGB24
    var result = new byte[totalPixels * 3];
    sourceData.AsSpan(0, Math.Min(sourceData.Length, result.Length)).CopyTo(result);
    return result;
  }
}
