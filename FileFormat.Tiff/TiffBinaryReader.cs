using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;

namespace FileFormat.Tiff;

/// <summary>Endian-aware TIFF binary parser. Reads header, IFDs, tags, and decompresses strip/tile data.</summary>
internal static class TiffBinaryReader {

  public static (TiffFile File, List<TiffPage> Pages) Parse(byte[] data) {
    if (data.Length < 8)
      throw new InvalidDataException("Data too small for a valid TIFF file.");

    var isBigEndian = _ReadByteOrder(data);
    var magic = _ReadUInt16(data, 2, isBigEndian);
    if (magic != TiffConstants.MagicNumber)
      throw new InvalidDataException($"Invalid TIFF magic number: expected {TiffConstants.MagicNumber}, got {magic}.");

    var ifdOffset = (int)_ReadUInt32(data, 4, isBigEndian);
    if (ifdOffset <= 0 || ifdOffset >= data.Length)
      throw new InvalidDataException($"Invalid first IFD offset: {ifdOffset}.");

    var (first, nextOffset) = _ParseIfd(data, ifdOffset, isBigEndian);
    var pages = new List<TiffPage>();

    while (nextOffset != 0 && nextOffset < data.Length) {
      try {
        var (page, next) = _ParseIfd(data, nextOffset, isBigEndian);
        pages.Add(new TiffPage {
          Width = page.Width,
          Height = page.Height,
          SamplesPerPixel = page.SamplesPerPixel,
          BitsPerSample = page.BitsPerSample,
          PixelData = page.PixelData,
          ColorMap = page.ColorMap,
          ColorMode = page.ColorMode,
        });
        nextOffset = next;
      } catch {
        break;
      }
    }

    return (first, pages);
  }

  private static bool _ReadByteOrder(byte[] data) {
    if (data[0] == 0x49 && data[1] == 0x49)
      return false; // Little-endian
    if (data[0] == 0x4D && data[1] == 0x4D)
      return true; // Big-endian
    throw new InvalidDataException($"Invalid byte order mark: 0x{data[0]:X2}{data[1]:X2}.");
  }

  private static ushort _ReadUInt16(byte[] data, int offset, bool bigEndian) {
    var span = data.AsSpan(offset, 2);
    return bigEndian ? BinaryPrimitives.ReadUInt16BigEndian(span) : BinaryPrimitives.ReadUInt16LittleEndian(span);
  }

  private static uint _ReadUInt32(byte[] data, int offset, bool bigEndian) {
    var span = data.AsSpan(offset, 4);
    return bigEndian ? BinaryPrimitives.ReadUInt32BigEndian(span) : BinaryPrimitives.ReadUInt32LittleEndian(span);
  }

  private static (TiffFile File, int NextIfdOffset) _ParseIfd(byte[] data, int ifdOffset, bool be) {
    if (ifdOffset + 2 > data.Length)
      throw new InvalidDataException("IFD offset beyond file bounds.");

    var entryCount = _ReadUInt16(data, ifdOffset, be);
    var pos = ifdOffset + 2;

    var width = 0;
    var height = 0;
    var bitsPerSample = 8;
    ushort compression = TiffConstants.CompressionNone;
    ushort photometric = TiffConstants.PhotometricRgb;
    var samplesPerPixel = 1;
    var rowsPerStrip = int.MaxValue;
    ushort predictor = TiffConstants.PredictorNone;
    ushort planarConfig = TiffConstants.PlanarConfigContig;

    uint[]? stripOffsets = null;
    uint[]? stripByteCounts = null;

    var tileWidth = 0;
    var tileHeight = 0;
    uint[]? tileOffsets = null;
    uint[]? tileByteCounts = null;

    ushort[]? colorMapRed = null;
    ushort[]? colorMapGreen = null;
    ushort[]? colorMapBlue = null;

    // Multiple BitsPerSample values (one per channel)
    int[]? bitsPerSampleArray = null;

    for (var i = 0; i < entryCount; ++i) {
      if (pos + 12 > data.Length)
        break;

      var tag = _ReadUInt16(data, pos, be);
      var type = _ReadUInt16(data, pos + 2, be);
      var count = _ReadUInt32(data, pos + 4, be);
      var valueOffset = pos + 8;

      switch (tag) {
        case TiffConstants.TagImageWidth:
          width = (int)_ReadTagValueScalar(data, valueOffset, type, count, be);
          break;
        case TiffConstants.TagImageLength:
          height = (int)_ReadTagValueScalar(data, valueOffset, type, count, be);
          break;
        case TiffConstants.TagBitsPerSample:
          if (count == 1) {
            bitsPerSample = (int)_ReadTagValueScalar(data, valueOffset, type, count, be);
          } else {
            bitsPerSampleArray = _ReadTagValuesInt(data, valueOffset, type, count, be);
            bitsPerSample = bitsPerSampleArray[0];
          }
          break;
        case TiffConstants.TagCompression:
          compression = (ushort)_ReadTagValueScalar(data, valueOffset, type, count, be);
          break;
        case TiffConstants.TagPhotometric:
          photometric = (ushort)_ReadTagValueScalar(data, valueOffset, type, count, be);
          break;
        case TiffConstants.TagSamplesPerPixel:
          samplesPerPixel = (int)_ReadTagValueScalar(data, valueOffset, type, count, be);
          break;
        case TiffConstants.TagRowsPerStrip:
          rowsPerStrip = (int)_ReadTagValueScalar(data, valueOffset, type, count, be);
          break;
        case TiffConstants.TagStripOffsets:
          stripOffsets = _ReadTagValuesUInt(data, valueOffset, type, count, be);
          break;
        case TiffConstants.TagStripByteCounts:
          stripByteCounts = _ReadTagValuesUInt(data, valueOffset, type, count, be);
          break;
        case TiffConstants.TagPredictor:
          predictor = (ushort)_ReadTagValueScalar(data, valueOffset, type, count, be);
          break;
        case TiffConstants.TagPlanarConfig:
          planarConfig = (ushort)_ReadTagValueScalar(data, valueOffset, type, count, be);
          break;
        case TiffConstants.TagTileWidth:
          tileWidth = (int)_ReadTagValueScalar(data, valueOffset, type, count, be);
          break;
        case TiffConstants.TagTileLength:
          tileHeight = (int)_ReadTagValueScalar(data, valueOffset, type, count, be);
          break;
        case TiffConstants.TagTileOffsets:
          tileOffsets = _ReadTagValuesUInt(data, valueOffset, type, count, be);
          break;
        case TiffConstants.TagTileByteCounts:
          tileByteCounts = _ReadTagValuesUInt(data, valueOffset, type, count, be);
          break;
        case TiffConstants.TagColorMap:
          _ReadColorMap(data, valueOffset, type, count, be, out colorMapRed, out colorMapGreen, out colorMapBlue);
          break;
      }

      pos += 12;
    }

    // Read next IFD offset
    var nextIfdOffset = pos + 4 <= data.Length ? (int)_ReadUInt32(data, pos, be) : 0;

    if (width <= 0 || height <= 0)
      throw new InvalidDataException($"Invalid dimensions: {width}x{height}.");

    // Read pixel data
    byte[] pixelData;
    var isTiled = tileWidth > 0 && tileHeight > 0 && tileOffsets != null;
    if (isTiled)
      pixelData = _ReadTiledData(data, width, height, samplesPerPixel, bitsPerSample,
        tileWidth, tileHeight, tileOffsets!, tileByteCounts, compression, predictor, be);
    else
      pixelData = _ReadStrippedData(data, width, height, samplesPerPixel, bitsPerSample,
        rowsPerStrip, stripOffsets, stripByteCounts, compression, predictor, be);

    // Build color map
    byte[]? colorMap = null;
    if (photometric == TiffConstants.PhotometricPalette && colorMapRed != null) {
      var paletteSize = 1 << bitsPerSample;
      colorMap = new byte[paletteSize * 3];
      for (var i = 0; i < paletteSize && i < colorMapRed.Length; ++i) {
        colorMap[i * 3] = (byte)(colorMapRed[i] / 257);
        colorMap[i * 3 + 1] = (byte)(colorMapGreen![i] / 257);
        colorMap[i * 3 + 2] = (byte)(colorMapBlue![i] / 257);
      }
    }

    var colorMode = _DetectColorMode(photometric, samplesPerPixel, bitsPerSample);

    var file = new TiffFile {
      Width = width,
      Height = height,
      SamplesPerPixel = samplesPerPixel,
      BitsPerSample = bitsPerSample,
      PixelData = pixelData,
      ColorMap = colorMap,
      ColorMode = colorMode,
    };

    return (file, nextIfdOffset);
  }

  private static byte[] _ReadStrippedData(byte[] data, int width, int height, int samplesPerPixel,
    int bitsPerSample, int rowsPerStrip, uint[]? stripOffsets, uint[]? stripByteCounts,
    ushort compression, ushort predictor, bool be) {
    var bytesPerRow = (width * samplesPerPixel * bitsPerSample + 7) / 8;
    var pixelData = new byte[bytesPerRow * height];

    if (stripOffsets == null || stripOffsets.Length == 0) {
      // No strip offsets — can't read data
      return pixelData;
    }

    if (rowsPerStrip <= 0)
      rowsPerStrip = height;

    var rowsDone = 0;
    for (var s = 0; s < stripOffsets.Length && rowsDone < height; ++s) {
      var offset = (int)stripOffsets[s];
      var byteCount = stripByteCounts != null && s < stripByteCounts.Length
        ? (int)stripByteCounts[s]
        : data.Length - offset;

      if (offset < 0 || offset >= data.Length)
        continue;

      byteCount = Math.Min(byteCount, data.Length - offset);
      var compressedData = data.AsSpan(offset, byteCount);

      var rowsInStrip = Math.Min(rowsPerStrip, height - rowsDone);
      var expectedStripSize = bytesPerRow * rowsInStrip;

      var decompressed = _Decompress(compressedData, expectedStripSize, compression);

      // Apply predictor reversal
      if (predictor == TiffConstants.PredictorHorizontal && bitsPerSample == 8)
        HorizontalDifferencing.Reverse(decompressed, bytesPerRow, rowsInStrip, samplesPerPixel);

      var copyLen = Math.Min(decompressed.Length, expectedStripSize);
      copyLen = Math.Min(copyLen, pixelData.Length - rowsDone * bytesPerRow);
      if (copyLen > 0)
        decompressed.AsSpan(0, copyLen).CopyTo(pixelData.AsSpan(rowsDone * bytesPerRow));

      rowsDone += rowsInStrip;
    }

    return pixelData;
  }

  private static byte[] _ReadTiledData(byte[] data, int width, int height, int samplesPerPixel,
    int bitsPerSample, int tileWidth, int tileHeight, uint[] tileOffsets, uint[]? tileByteCounts,
    ushort compression, ushort predictor, bool be) {
    var bytesPerPixel = (samplesPerPixel * bitsPerSample + 7) / 8;
    var bytesPerRow = width * bytesPerPixel;
    var pixelData = new byte[bytesPerRow * height];
    var tileBytesPerRow = tileWidth * bytesPerPixel;
    var tileSize = tileBytesPerRow * tileHeight;

    var tileIndex = 0;
    for (var ty = 0; ty < height; ty += tileHeight)
    for (var tx = 0; tx < width; tx += tileWidth) {
      if (tileIndex >= tileOffsets.Length)
        break;

      var offset = (int)tileOffsets[tileIndex];
      var byteCount = tileByteCounts != null && tileIndex < tileByteCounts.Length
        ? (int)tileByteCounts[tileIndex]
        : data.Length - offset;

      if (offset < 0 || offset >= data.Length) {
        ++tileIndex;
        continue;
      }

      byteCount = Math.Min(byteCount, data.Length - offset);
      var compressedData = data.AsSpan(offset, byteCount);

      var tileData = _Decompress(compressedData, tileSize, compression);

      if (predictor == TiffConstants.PredictorHorizontal && bitsPerSample == 8)
        HorizontalDifferencing.Reverse(tileData, tileBytesPerRow, tileHeight, samplesPerPixel);

      var rowsInTile = Math.Min(tileHeight, height - ty);
      var colsInTile = Math.Min(tileWidth, width - tx);
      var bytesToCopy = colsInTile * bytesPerPixel;

      for (var r = 0; r < rowsInTile; ++r) {
        var srcOffset2 = r * tileBytesPerRow;
        var dstOffset = (ty + r) * bytesPerRow + tx * bytesPerPixel;
        if (srcOffset2 + bytesToCopy <= tileData.Length && dstOffset + bytesToCopy <= pixelData.Length)
          tileData.AsSpan(srcOffset2, bytesToCopy).CopyTo(pixelData.AsSpan(dstOffset));
      }

      ++tileIndex;
    }

    return pixelData;
  }

  private static byte[] _Decompress(ReadOnlySpan<byte> data, int expectedLength, ushort compression) => compression switch {
    TiffConstants.CompressionNone => data.Length >= expectedLength ? data[..expectedLength].ToArray() : _PadCopy(data, expectedLength),
    TiffConstants.CompressionPackBits => PackBitsCompressor.Decompress(data, expectedLength),
    TiffConstants.CompressionLzw => TiffLzwCompressor.Decompress(data, expectedLength),
    TiffConstants.CompressionDeflate => TiffDeflateHelper.Decompress(data, expectedLength),
    _ => throw new InvalidDataException($"Unsupported TIFF compression: {compression}."),
  };

  private static byte[] _PadCopy(ReadOnlySpan<byte> data, int expectedLength) {
    var result = new byte[expectedLength];
    data.CopyTo(result.AsSpan(0, Math.Min(data.Length, expectedLength)));
    return result;
  }

  private static uint _ReadTagValueScalar(byte[] data, int valueOffset, ushort type, uint count, bool be) {
    var totalSize = count * (uint)TiffConstants.TypeSize(type);
    var actualOffset = totalSize > 4 ? (int)_ReadUInt32(data, valueOffset, be) : valueOffset;

    if (actualOffset + TiffConstants.TypeSize(type) > data.Length)
      return 0;

    return type switch {
      TiffConstants.TypeByte => data[actualOffset],
      TiffConstants.TypeShort => _ReadUInt16(data, actualOffset, be),
      TiffConstants.TypeLong => _ReadUInt32(data, actualOffset, be),
      _ => _ReadUInt32(data, actualOffset, be),
    };
  }

  private static uint[] _ReadTagValuesUInt(byte[] data, int valueOffset, ushort type, uint count, bool be) {
    var typeSize = TiffConstants.TypeSize(type);
    var totalSize = count * (uint)typeSize;
    var actualOffset = totalSize > 4 ? (int)_ReadUInt32(data, valueOffset, be) : valueOffset;

    var result = new uint[count];
    for (var i = 0; i < count; ++i) {
      var pos = actualOffset + i * typeSize;
      if (pos + typeSize > data.Length)
        break;
      result[i] = type switch {
        TiffConstants.TypeByte => data[pos],
        TiffConstants.TypeShort => _ReadUInt16(data, pos, be),
        TiffConstants.TypeLong => _ReadUInt32(data, pos, be),
        _ => _ReadUInt32(data, pos, be),
      };
    }

    return result;
  }

  private static int[] _ReadTagValuesInt(byte[] data, int valueOffset, ushort type, uint count, bool be) {
    var uints = _ReadTagValuesUInt(data, valueOffset, type, count, be);
    var result = new int[uints.Length];
    for (var i = 0; i < uints.Length; ++i)
      result[i] = (int)uints[i];
    return result;
  }

  private static void _ReadColorMap(byte[] data, int valueOffset, ushort type, uint count, bool be,
    out ushort[]? red, out ushort[]? green, out ushort[]? blue) {
    // ColorMap has 3*paletteSize SHORT values: all reds, then all greens, then all blues
    var paletteSize = (int)(count / 3);
    var typeSize = TiffConstants.TypeSize(type);
    var totalSize = count * (uint)typeSize;
    var actualOffset = totalSize > 4 ? (int)_ReadUInt32(data, valueOffset, be) : valueOffset;

    red = new ushort[paletteSize];
    green = new ushort[paletteSize];
    blue = new ushort[paletteSize];

    for (var i = 0; i < paletteSize; ++i) {
      var rPos = actualOffset + i * 2;
      var gPos = actualOffset + (paletteSize + i) * 2;
      var bPos = actualOffset + (paletteSize * 2 + i) * 2;

      if (rPos + 2 <= data.Length) red[i] = _ReadUInt16(data, rPos, be);
      if (gPos + 2 <= data.Length) green[i] = _ReadUInt16(data, gPos, be);
      if (bPos + 2 <= data.Length) blue[i] = _ReadUInt16(data, bPos, be);
    }
  }

  private static TiffColorMode _DetectColorMode(ushort photometric, int samplesPerPixel, int bitsPerSample) {
    if (photometric == TiffConstants.PhotometricPalette)
      return TiffColorMode.Palette;

    if (photometric is TiffConstants.PhotometricMinIsBlack or TiffConstants.PhotometricMinIsWhite) {
      if (bitsPerSample == 1)
        return TiffColorMode.BiLevel;
      return TiffColorMode.Grayscale;
    }

    if (photometric == TiffConstants.PhotometricRgb)
      return TiffColorMode.Rgb;

    return TiffColorMode.Original;
  }
}
