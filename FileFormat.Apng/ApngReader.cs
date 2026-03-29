using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using FileFormat.Png;

namespace FileFormat.Apng;

/// <summary>Reads and parses APNG (Animated PNG) files.</summary>
public static class ApngReader {

  private static readonly byte[] _PngSignature = [137, 80, 78, 71, 13, 10, 26, 10];

  /// <summary>Read an APNG file from disk.</summary>
  public static ApngFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("APNG file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  /// <summary>Read an APNG from a stream.</summary>
  public static ApngFile FromStream(Stream stream) {
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

  /// <summary>Read an APNG from a byte array.</summary>
  public static ApngFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < 8 + 25)
      throw new InvalidDataException("Data too short to be a valid APNG file.");

    if (!_CheckSignature(data))
      throw new InvalidDataException("Invalid PNG signature.");

    var offset = 8;
    int width = 0, height = 0, bitDepth = 0;
    var colorType = PngColorType.RGB;
    byte[]? palette = null;
    byte[]? transparency = null;
    var numPlays = 0;
    var hasActl = false;

    var frames = new List<_FrameBuilder>();
    _FrameBuilder? currentFrame = null;

    while (offset + 8 <= data.Length) {
      var chunkLength = BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(offset));
      var chunkType = Encoding.ASCII.GetString(data, offset + 4, 4);
      offset += 8;

      if (chunkLength < 0 || offset + chunkLength + 4 > data.Length)
        break;

      var chunkData = data.AsSpan(offset, chunkLength);

      switch (chunkType) {
        case "IHDR":
          width = BinaryPrimitives.ReadInt32BigEndian(chunkData);
          height = BinaryPrimitives.ReadInt32BigEndian(chunkData[4..]);
          bitDepth = chunkData[8];
          colorType = (PngColorType)chunkData[9];
          break;
        case "PLTE":
          palette = chunkData.ToArray();
          break;
        case "tRNS":
          transparency = chunkData.ToArray();
          break;
        case "acTL":
          hasActl = true;
          var actl = ApngActl.ReadFrom(chunkData);
          numPlays = actl.NumPlays;
          break;
        case "fcTL":
          var fctl = ApngFctl.ReadFrom(chunkData);
          currentFrame = new _FrameBuilder {
            Width = fctl.Width,
            Height = fctl.Height,
            XOffset = fctl.XOffset,
            YOffset = fctl.YOffset,
            DelayNumerator = fctl.DelayNum,
            DelayDenominator = fctl.DelayDen,
            DisposeOp = (ApngDisposeOp)fctl.DisposeOp,
            BlendOp = (ApngBlendOp)fctl.BlendOp
          };
          frames.Add(currentFrame);
          break;
        case "IDAT":
          if (currentFrame != null)
            currentFrame.CompressedChunks.Add(chunkData.ToArray());
          else {
            // IDAT without preceding fcTL: treat as single-frame APNG
            currentFrame = new _FrameBuilder {
              Width = width,
              Height = height,
              XOffset = 0,
              YOffset = 0,
              DelayNumerator = 0,
              DelayDenominator = 1,
              DisposeOp = ApngDisposeOp.None,
              BlendOp = ApngBlendOp.Source
            };
            currentFrame.CompressedChunks.Add(chunkData.ToArray());
            frames.Add(currentFrame);
          }
          break;
        case "fdAT":
          if (currentFrame != null && chunkLength >= 4) {
            // Skip 4-byte sequence number, rest is IDAT-equivalent data
            var fdatPayload = chunkData[4..].ToArray();
            currentFrame.CompressedChunks.Add(fdatPayload);
          }
          break;
        case "IEND":
          break;
      }

      offset += chunkLength;
      offset += 4; // CRC

      if (chunkType == "IEND")
        break;
    }

    if (!hasActl && frames.Count == 0) {
      // No animation control and no frames parsed, not a valid APNG
      return new ApngFile {
        Width = width,
        Height = height,
        BitDepth = bitDepth,
        ColorType = colorType,
        NumPlays = 0,
        Frames = [],
        Palette = palette,
        Transparency = transparency
      };
    }

    var resultFrames = new List<ApngFrame>(frames.Count);
    foreach (var builder in frames) {
      var pixelData = _DecompressAndDefilter(
        _ConcatenateChunks(builder.CompressedChunks),
        builder.Width,
        builder.Height,
        bitDepth,
        colorType
      );

      resultFrames.Add(new ApngFrame {
        Width = builder.Width,
        Height = builder.Height,
        XOffset = builder.XOffset,
        YOffset = builder.YOffset,
        DelayNumerator = builder.DelayNumerator,
        DelayDenominator = builder.DelayDenominator,
        DisposeOp = builder.DisposeOp,
        BlendOp = builder.BlendOp,
        PixelData = pixelData
      });
    }

    return new ApngFile {
      Width = width,
      Height = height,
      BitDepth = bitDepth,
      ColorType = colorType,
      NumPlays = numPlays,
      Frames = resultFrames,
      Palette = palette,
      Transparency = transparency
    };
  }

  private static bool _CheckSignature(byte[] data) {
    if (data.Length < 8)
      return false;

    for (var i = 0; i < 8; ++i)
      if (data[i] != _PngSignature[i])
        return false;

    return true;
  }

  private static byte[] _ConcatenateChunks(List<byte[]> chunks) {
    var totalLength = 0;
    foreach (var chunk in chunks)
      totalLength += chunk.Length;

    var result = new byte[totalLength];
    var offset = 0;
    foreach (var chunk in chunks) {
      Buffer.BlockCopy(chunk, 0, result, offset, chunk.Length);
      offset += chunk.Length;
    }

    return result;
  }

  private static byte[][] _DecompressAndDefilter(byte[] compressedData, int width, int height, int bitDepth, PngColorType colorType) {
    byte[] rawData;
    using (var input = new MemoryStream(compressedData))
    using (var zlib = new ZLibStream(input, CompressionMode.Decompress))
    using (var output = new MemoryStream()) {
      zlib.CopyTo(output);
      rawData = output.ToArray();
    }

    return _DefilterScanlines(rawData, width, height, bitDepth, colorType);
  }

  private static byte[][] _DefilterScanlines(byte[] rawData, int width, int height, int bitDepth, PngColorType colorType) {
    var samplesPerPixel = _GetSamplesPerPixel(colorType);
    var bitsPerPixel = bitDepth * samplesPerPixel;
    var bytesPerScanline = (width * bitsPerPixel + 7) / 8;
    var filterStride = Math.Max(1, samplesPerPixel * bitDepth / 8);

    var result = new byte[height][];
    var offset = 0;

    byte[]? previousRow = null;
    for (var y = 0; y < height; ++y) {
      if (offset >= rawData.Length)
        break;

      var filterByte = rawData[offset++];
      var filteredRow = new byte[bytesPerScanline];
      var available = Math.Min(bytesPerScanline, rawData.Length - offset);
      Buffer.BlockCopy(rawData, offset, filteredRow, 0, available);
      offset += bytesPerScanline;

      result[y] = _ReverseFilter((PngFilterType)filterByte, filteredRow, previousRow, filterStride);
      previousRow = result[y];
    }

    return result;
  }

  private static byte[] _ReverseFilter(PngFilterType filterType, byte[] filtered, byte[]? previousRow, int stride) {
    var result = new byte[filtered.Length];

    switch (filterType) {
      case PngFilterType.None:
        Buffer.BlockCopy(filtered, 0, result, 0, filtered.Length);
        break;
      case PngFilterType.Sub:
        for (var i = 0; i < filtered.Length; ++i)
          result[i] = (byte)(filtered[i] + (i >= stride ? result[i - stride] : 0));
        break;
      case PngFilterType.Up:
        for (var i = 0; i < filtered.Length; ++i)
          result[i] = (byte)(filtered[i] + (previousRow != null ? previousRow[i] : 0));
        break;
      case PngFilterType.Average:
        for (var i = 0; i < filtered.Length; ++i) {
          var left = i >= stride ? result[i - stride] : 0;
          var above = previousRow != null ? previousRow[i] : 0;
          result[i] = (byte)(filtered[i] + ((left + above) >> 1));
        }
        break;
      case PngFilterType.Paeth:
        for (var i = 0; i < filtered.Length; ++i) {
          var a = i >= stride ? result[i - stride] : 0;
          var b = previousRow != null ? previousRow[i] : 0;
          var c = i >= stride && previousRow != null ? previousRow[i - stride] : 0;
          var p = a + b - c;
          var pa = Math.Abs(p - a);
          var pb = Math.Abs(p - b);
          var pc = Math.Abs(p - c);
          result[i] = (byte)(filtered[i] + (pa <= pb && pa <= pc ? a : pb <= pc ? b : c));
        }
        break;
    }

    return result;
  }

  private static int _GetSamplesPerPixel(PngColorType colorType) => colorType switch {
    PngColorType.Grayscale => 1,
    PngColorType.GrayscaleAlpha => 2,
    PngColorType.RGB => 3,
    PngColorType.RGBA => 4,
    PngColorType.Palette => 1,
    _ => throw new ArgumentException($"Invalid color type: {colorType}")
  };

  private sealed class _FrameBuilder {
    public int Width;
    public int Height;
    public int XOffset;
    public int YOffset;
    public ushort DelayNumerator;
    public ushort DelayDenominator;
    public ApngDisposeOp DisposeOp;
    public ApngBlendOp BlendOp;
    public readonly List<byte[]> CompressedChunks = [];
  }
}
