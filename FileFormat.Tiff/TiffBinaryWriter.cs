using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;

namespace FileFormat.Tiff;

/// <summary>Two-pass TIFF byte-stream assembly: compress strips/tiles, calculate layout, assemble.</summary>
internal static class TiffBinaryWriter {

  public static byte[] Assemble(
    byte[] pixelData,
    int width,
    int height,
    int samplesPerPixel,
    int bitsPerSample,
    TiffCompression compression,
    TiffPredictor predictor,
    int stripRowCount,
    int zopfliIterations,
    ushort photometric,
    byte[]? colorMap = null,
    int tileWidth = 0,
    int tileHeight = 0
  ) {
    var isTiled = tileWidth > 0 && tileHeight > 0;
    var bytesPerRow = (width * samplesPerPixel * bitsPerSample + 7) / 8;

    // Pass 1: Compress all strips/tiles
    List<byte[]> compressedChunks;
    int chunkCount;
    if (isTiled)
      compressedChunks = _CompressTiles(pixelData, width, height, samplesPerPixel, bitsPerSample,
        tileWidth, tileHeight, compression, predictor, zopfliIterations);
    else
      compressedChunks = _CompressStrips(pixelData, width, height, samplesPerPixel, bitsPerSample,
        bytesPerRow, stripRowCount, compression, predictor, zopfliIterations);

    chunkCount = compressedChunks.Count;

    // Build IFD tags
    var tags = new List<(ushort Tag, ushort Type, uint Count, byte[] Value)>();
    _AddTag(tags, TiffConstants.TagImageWidth, TiffConstants.TypeLong, 1, _UInt32Bytes(width));
    _AddTag(tags, TiffConstants.TagImageLength, TiffConstants.TypeLong, 1, _UInt32Bytes(height));

    if (samplesPerPixel > 1) {
      var bpsBytes = new byte[samplesPerPixel * 2];
      for (var i = 0; i < samplesPerPixel; ++i)
        BinaryPrimitives.WriteUInt16LittleEndian(bpsBytes.AsSpan(i * 2), (ushort)bitsPerSample);
      _AddTag(tags, TiffConstants.TagBitsPerSample, TiffConstants.TypeShort, (uint)samplesPerPixel, bpsBytes);
    } else {
      _AddTag(tags, TiffConstants.TagBitsPerSample, TiffConstants.TypeShort, 1, _UInt16Bytes(bitsPerSample));
    }

    _AddTag(tags, TiffConstants.TagCompression, TiffConstants.TypeShort, 1, _UInt16Bytes(_MapCompression(compression)));
    _AddTag(tags, TiffConstants.TagPhotometric, TiffConstants.TypeShort, 1, _UInt16Bytes(photometric));
    _AddTag(tags, TiffConstants.TagOrientation, TiffConstants.TypeShort, 1, _UInt16Bytes(TiffConstants.OrientationTopLeft));
    _AddTag(tags, TiffConstants.TagSamplesPerPixel, TiffConstants.TypeShort, 1, _UInt16Bytes(samplesPerPixel));

    if (isTiled) {
      _AddTag(tags, TiffConstants.TagTileWidth, TiffConstants.TypeLong, 1, _UInt32Bytes(tileWidth));
      _AddTag(tags, TiffConstants.TagTileLength, TiffConstants.TypeLong, 1, _UInt32Bytes(tileHeight));
    } else {
      _AddTag(tags, TiffConstants.TagRowsPerStrip, TiffConstants.TypeLong, 1, _UInt32Bytes(stripRowCount));
    }

    _AddTag(tags, TiffConstants.TagPlanarConfig, TiffConstants.TypeShort, 1, _UInt16Bytes(TiffConstants.PlanarConfigContig));

    if (predictor == TiffPredictor.HorizontalDifferencing &&
        compression is not (TiffCompression.None or TiffCompression.PackBits))
      _AddTag(tags, TiffConstants.TagPredictor, TiffConstants.TypeShort, 1, _UInt16Bytes(TiffConstants.PredictorHorizontal));

    // ColorMap tag
    byte[]? colorMapBytes = null;
    if (colorMap != null && photometric == TiffConstants.PhotometricPalette) {
      var paletteSize = 1 << bitsPerSample;
      colorMapBytes = new byte[paletteSize * 3 * 2]; // 3 arrays of uint16
      for (var i = 0; i < paletteSize && i * 3 + 2 < colorMap.Length; ++i) {
        BinaryPrimitives.WriteUInt16LittleEndian(colorMapBytes.AsSpan(i * 2), (ushort)(colorMap[i * 3] * 257));
        BinaryPrimitives.WriteUInt16LittleEndian(colorMapBytes.AsSpan((paletteSize + i) * 2), (ushort)(colorMap[i * 3 + 1] * 257));
        BinaryPrimitives.WriteUInt16LittleEndian(colorMapBytes.AsSpan((paletteSize * 2 + i) * 2), (ushort)(colorMap[i * 3 + 2] * 257));
      }
      _AddTag(tags, TiffConstants.TagColorMap, TiffConstants.TypeShort, (uint)(paletteSize * 3), colorMapBytes);
    }

    // Placeholder tags for strip/tile offsets and byte counts (will be filled in during assembly)
    var offsetsBytes = new byte[chunkCount * 4];
    var byteCountsBytes = new byte[chunkCount * 4];
    for (var i = 0; i < chunkCount; ++i)
      BinaryPrimitives.WriteUInt32LittleEndian(byteCountsBytes.AsSpan(i * 4), (uint)compressedChunks[i].Length);

    if (isTiled) {
      _AddTag(tags, TiffConstants.TagTileOffsets, TiffConstants.TypeLong, (uint)chunkCount, offsetsBytes);
      _AddTag(tags, TiffConstants.TagTileByteCounts, TiffConstants.TypeLong, (uint)chunkCount, byteCountsBytes);
    } else {
      _AddTag(tags, TiffConstants.TagStripOffsets, TiffConstants.TypeLong, (uint)chunkCount, offsetsBytes);
      _AddTag(tags, TiffConstants.TagStripByteCounts, TiffConstants.TypeLong, (uint)chunkCount, byteCountsBytes);
    }

    // Sort tags by tag number (TIFF spec requirement)
    tags.Sort((a, b) => a.Tag.CompareTo(b.Tag));

    // Pass 2: Calculate layout and assemble
    return _AssembleFile(tags, compressedChunks, isTiled);
  }

  public static byte[] AssembleMultiPage(TiffFile file, TiffCompression compression, TiffPredictor predictor,
    int stripRowCount, int zopfliIterations, int tileWidth, int tileHeight) {
    // Collect all pages (first page + additional pages)
    var allPages = new List<(byte[] pixelData, int width, int height, int spp, int bps, ushort photometric, byte[]? colorMap)> {
      (file.PixelData, file.Width, file.Height, file.SamplesPerPixel, file.BitsPerSample,
        _DeterminePhotometric(file.ColorMap, file.SamplesPerPixel), file.ColorMap)
    };

    foreach (var page in file.Pages)
      allPages.Add((page.PixelData, page.Width, page.Height, page.SamplesPerPixel, page.BitsPerSample,
        _DeterminePhotometric(page.ColorMap, page.SamplesPerPixel), page.ColorMap));

    // For each page, build its complete byte representation then stitch together
    using var ms = new MemoryStream();

    // Write header
    ms.WriteByte(0x49); // 'I' (little-endian)
    ms.WriteByte(0x49); // 'I'
    _WriteUInt16(ms, TiffConstants.MagicNumber);
    _WriteUInt32(ms, 8); // First IFD offset (right after header)

    for (var pageIdx = 0; pageIdx < allPages.Count; ++pageIdx) {
      var (pixelData, width, height, spp, bps, photometric, colorMap) = allPages[pageIdx];
      var isLastPage = pageIdx == allPages.Count - 1;
      _WriteIfd(ms, pixelData, width, height, spp, bps, compression, predictor,
        stripRowCount, zopfliIterations, photometric, colorMap, tileWidth, tileHeight, isLastPage);
    }

    return ms.ToArray();
  }

  private static void _WriteIfd(MemoryStream ms, byte[] pixelData, int width, int height,
    int samplesPerPixel, int bitsPerSample, TiffCompression compression, TiffPredictor predictor,
    int stripRowCount, int zopfliIterations, ushort photometric, byte[]? colorMap,
    int tileWidth, int tileHeight, bool isLast) {
    var isTiled = tileWidth > 0 && tileHeight > 0;
    var bytesPerRow = (width * samplesPerPixel * bitsPerSample + 7) / 8;

    // Compress all chunks
    List<byte[]> compressedChunks;
    if (isTiled)
      compressedChunks = _CompressTiles(pixelData, width, height, samplesPerPixel, bitsPerSample,
        tileWidth, tileHeight, compression, predictor, zopfliIterations);
    else
      compressedChunks = _CompressStrips(pixelData, width, height, samplesPerPixel, bitsPerSample,
        bytesPerRow, stripRowCount, compression, predictor, zopfliIterations);

    var chunkCount = compressedChunks.Count;

    // Build tag list
    var tags = new List<(ushort Tag, ushort Type, uint Count, uint InlineValue, byte[]? ExternalData)>();

    tags.Add((TiffConstants.TagImageWidth, TiffConstants.TypeLong, 1, (uint)width, null));
    tags.Add((TiffConstants.TagImageLength, TiffConstants.TypeLong, 1, (uint)height, null));

    if (samplesPerPixel > 1) {
      var bpsBytes = new byte[samplesPerPixel * 2];
      for (var i = 0; i < samplesPerPixel; ++i)
        BinaryPrimitives.WriteUInt16LittleEndian(bpsBytes.AsSpan(i * 2), (ushort)bitsPerSample);
      tags.Add((TiffConstants.TagBitsPerSample, TiffConstants.TypeShort, (uint)samplesPerPixel, 0, bpsBytes));
    } else {
      tags.Add((TiffConstants.TagBitsPerSample, TiffConstants.TypeShort, 1, (uint)bitsPerSample, null));
    }

    tags.Add((TiffConstants.TagCompression, TiffConstants.TypeShort, 1, _MapCompression(compression), null));
    tags.Add((TiffConstants.TagPhotometric, TiffConstants.TypeShort, 1, photometric, null));
    tags.Add((TiffConstants.TagOrientation, TiffConstants.TypeShort, 1, TiffConstants.OrientationTopLeft, null));
    tags.Add((TiffConstants.TagSamplesPerPixel, TiffConstants.TypeShort, 1, (uint)samplesPerPixel, null));

    if (isTiled) {
      tags.Add((TiffConstants.TagTileWidth, TiffConstants.TypeLong, 1, (uint)tileWidth, null));
      tags.Add((TiffConstants.TagTileLength, TiffConstants.TypeLong, 1, (uint)tileHeight, null));
    } else {
      tags.Add((TiffConstants.TagRowsPerStrip, TiffConstants.TypeLong, 1, (uint)stripRowCount, null));
    }

    tags.Add((TiffConstants.TagPlanarConfig, TiffConstants.TypeShort, 1, TiffConstants.PlanarConfigContig, null));

    if (predictor == TiffPredictor.HorizontalDifferencing &&
        compression is not (TiffCompression.None or TiffCompression.PackBits))
      tags.Add((TiffConstants.TagPredictor, TiffConstants.TypeShort, 1, TiffConstants.PredictorHorizontal, null));

    if (colorMap != null && photometric == TiffConstants.PhotometricPalette) {
      var paletteSize = 1 << bitsPerSample;
      var cmBytes = new byte[paletteSize * 3 * 2];
      for (var i = 0; i < paletteSize && i * 3 + 2 < colorMap.Length; ++i) {
        BinaryPrimitives.WriteUInt16LittleEndian(cmBytes.AsSpan(i * 2), (ushort)(colorMap[i * 3] * 257));
        BinaryPrimitives.WriteUInt16LittleEndian(cmBytes.AsSpan((paletteSize + i) * 2), (ushort)(colorMap[i * 3 + 1] * 257));
        BinaryPrimitives.WriteUInt16LittleEndian(cmBytes.AsSpan((paletteSize * 2 + i) * 2), (ushort)(colorMap[i * 3 + 2] * 257));
      }
      tags.Add((TiffConstants.TagColorMap, TiffConstants.TypeShort, (uint)(paletteSize * 3), 0, cmBytes));
    }

    // Offset/bytecount placeholders (will patch later)
    var offsetTag = isTiled ? TiffConstants.TagTileOffsets : TiffConstants.TagStripOffsets;
    var bcTag = isTiled ? TiffConstants.TagTileByteCounts : TiffConstants.TagStripByteCounts;
    var byteCountsBytes = new byte[chunkCount * 4];
    for (var i = 0; i < chunkCount; ++i)
      BinaryPrimitives.WriteUInt32LittleEndian(byteCountsBytes.AsSpan(i * 4), (uint)compressedChunks[i].Length);

    tags.Add((offsetTag, TiffConstants.TypeLong, (uint)chunkCount, 0, new byte[chunkCount * 4]));
    tags.Add((bcTag, TiffConstants.TypeLong, (uint)chunkCount, 0, byteCountsBytes));

    tags.Sort((a, b) => a.Tag.CompareTo(b.Tag));

    // Calculate IFD size: 2 (count) + entries * 12 + 4 (next IFD offset)
    var ifdSize = 2 + tags.Count * 12 + 4;
    var ifdStart = (int)ms.Position;

    // Calculate external data area start
    var externalStart = ifdStart + ifdSize;

    // First, collect external data and figure out offsets for strip/tile data
    var externalParts = new List<(int tagIndex, byte[] data)>();
    var offsetTagIndex = -1;
    var bcTagIndex = -1;

    for (var i = 0; i < tags.Count; ++i) {
      if (tags[i].ExternalData != null) {
        var typeSize = TiffConstants.TypeSize(tags[i].Type);
        if (tags[i].Count * typeSize > 4)
          externalParts.Add((i, tags[i].ExternalData!));
      }

      if (tags[i].Tag == offsetTag)
        offsetTagIndex = i;
      if (tags[i].Tag == bcTag)
        bcTagIndex = i;
    }

    // Calculate all external data offsets
    var currentExternalOffset = externalStart;
    var externalOffsets = new Dictionary<int, int>();
    foreach (var (tagIndex, data) in externalParts) {
      externalOffsets[tagIndex] = currentExternalOffset;
      currentExternalOffset += data.Length;
    }

    // Now we know where pixel data starts
    var pixelDataStart = currentExternalOffset;

    // Build strip/tile offset array
    var offsetsData = new byte[chunkCount * 4];
    var currentChunkOffset = pixelDataStart;
    for (var i = 0; i < chunkCount; ++i) {
      BinaryPrimitives.WriteUInt32LittleEndian(offsetsData.AsSpan(i * 4), (uint)currentChunkOffset);
      currentChunkOffset += compressedChunks[i].Length;
    }

    // Patch the offsets tag's external data
    if (offsetTagIndex >= 0) {
      var tag = tags[offsetTagIndex];
      tags[offsetTagIndex] = (tag.Tag, tag.Type, tag.Count, tag.InlineValue, offsetsData);
      // Also update the external parts
      for (var i = 0; i < externalParts.Count; ++i) {
        if (externalParts[i].tagIndex == offsetTagIndex)
          externalParts[i] = (offsetTagIndex, offsetsData);
      }
    }

    // Write IFD entry count
    var countBytes = new byte[2];
    BinaryPrimitives.WriteUInt16LittleEndian(countBytes, (ushort)tags.Count);
    ms.Write(countBytes);

    // Write IFD entries
    foreach (var (tag, type, count, inlineValue, externalData) in tags) {
      var entryBytes = new byte[12];
      BinaryPrimitives.WriteUInt16LittleEndian(entryBytes.AsSpan(0), tag);
      BinaryPrimitives.WriteUInt16LittleEndian(entryBytes.AsSpan(2), type);
      BinaryPrimitives.WriteUInt32LittleEndian(entryBytes.AsSpan(4), count);

      var typeSize = TiffConstants.TypeSize(type);
      if (count * typeSize <= 4) {
        // Inline value
        if (type == TiffConstants.TypeShort && count == 1)
          BinaryPrimitives.WriteUInt16LittleEndian(entryBytes.AsSpan(8), (ushort)inlineValue);
        else
          BinaryPrimitives.WriteUInt32LittleEndian(entryBytes.AsSpan(8), inlineValue);
      } else {
        // External offset
        var tagIdx = tags.IndexOf((tag, type, count, inlineValue, externalData));
        if (externalOffsets.TryGetValue(tagIdx, out var extOffset))
          BinaryPrimitives.WriteUInt32LittleEndian(entryBytes.AsSpan(8), (uint)extOffset);
      }

      ms.Write(entryBytes);
    }

    // Write next IFD offset (0 = last page, or will be patched)
    if (isLast) {
      _WriteUInt32(ms, 0);
    } else {
      // Calculate where next IFD will start (after all external data + pixel data)
      var totalChunkSize = 0;
      foreach (var chunk in compressedChunks)
        totalChunkSize += chunk.Length;

      var nextIfdOffset = pixelDataStart + totalChunkSize;
      _WriteUInt32(ms, (uint)nextIfdOffset);
    }

    // Write external data
    foreach (var (_, data) in externalParts)
      ms.Write(data);

    // Write pixel data (compressed chunks)
    foreach (var chunk in compressedChunks)
      ms.Write(chunk);
  }

  private static byte[] _AssembleFile(
    List<(ushort Tag, ushort Type, uint Count, byte[] Value)> tags,
    List<byte[]> compressedChunks, bool isTiled) {
    using var ms = new MemoryStream();

    // Write header (LE)
    ms.WriteByte(0x49); // 'I'
    ms.WriteByte(0x49); // 'I'
    _WriteUInt16(ms, TiffConstants.MagicNumber);
    _WriteUInt32(ms, 8); // First IFD at offset 8

    // IFD starts at offset 8
    var tagCount = tags.Count;
    var ifdSize = 2 + tagCount * 12 + 4; // count + entries + next IFD pointer

    // Calculate external data area start
    var externalStart = 8 + ifdSize;

    // Collect tags that need external data (value > 4 bytes)
    var externalParts = new List<(int tagIndex, byte[] data)>();
    for (var i = 0; i < tags.Count; ++i) {
      var (_, type, count, value) = tags[i];
      var totalSize = count * (uint)TiffConstants.TypeSize(type);
      if (totalSize > 4)
        externalParts.Add((i, value));
    }

    // Calculate external offsets
    var currentOffset = externalStart;
    var externalOffsets = new Dictionary<int, int>();
    foreach (var (idx, data) in externalParts) {
      externalOffsets[idx] = currentOffset;
      currentOffset += data.Length;
    }

    // Calculate pixel data start
    var pixelDataStart = currentOffset;

    // Build strip/tile offsets
    var offsetTag = isTiled ? TiffConstants.TagTileOffsets : TiffConstants.TagStripOffsets;
    var chunkOffset = pixelDataStart;
    for (var i = 0; i < tags.Count; ++i) {
      if (tags[i].Tag != offsetTag)
        continue;

      var offsetsBytes = new byte[compressedChunks.Count * 4];
      for (var j = 0; j < compressedChunks.Count; ++j) {
        BinaryPrimitives.WriteUInt32LittleEndian(offsetsBytes.AsSpan(j * 4), (uint)chunkOffset);
        chunkOffset += compressedChunks[j].Length;
      }

      tags[i] = (tags[i].Tag, tags[i].Type, tags[i].Count, offsetsBytes);

      // Update external parts
      for (var k = 0; k < externalParts.Count; ++k) {
        if (externalParts[k].tagIndex == i)
          externalParts[k] = (i, offsetsBytes);
      }

      break;
    }

    // Write IFD entry count
    _WriteUInt16(ms, (ushort)tagCount);

    // Write IFD entries
    for (var i = 0; i < tags.Count; ++i) {
      var (tag, type, count, value) = tags[i];
      _WriteUInt16(ms, tag);
      _WriteUInt16(ms, type);
      _WriteUInt32(ms, count);

      var totalSize = count * (uint)TiffConstants.TypeSize(type);
      if (totalSize <= 4) {
        // Inline: pad to 4 bytes
        var padded = new byte[4];
        var copyLen = Math.Min(value.Length, 4);
        value.AsSpan(0, copyLen).CopyTo(padded);
        ms.Write(padded);
      } else {
        // External offset
        _WriteUInt32(ms, (uint)externalOffsets[i]);
      }
    }

    // Next IFD offset (0 = single page)
    _WriteUInt32(ms, 0);

    // Write external data
    foreach (var (_, data) in externalParts)
      ms.Write(data);

    // Write pixel data
    foreach (var chunk in compressedChunks)
      ms.Write(chunk);

    return ms.ToArray();
  }

  private static List<byte[]> _CompressStrips(byte[] pixelData, int width, int height,
    int samplesPerPixel, int bitsPerSample, int bytesPerRow, int stripRowCount,
    TiffCompression compression, TiffPredictor predictor, int zopfliIterations) {
    var chunks = new List<byte[]>();

    for (var row = 0; row < height; row += stripRowCount) {
      var rowsInStrip = Math.Min(stripRowCount, height - row);
      var stripSize = bytesPerRow * rowsInStrip;
      var stripData = new byte[stripSize];
      var srcOffset = row * bytesPerRow;
      var copyLen = Math.Min(stripSize, pixelData.Length - srcOffset);
      if (copyLen > 0)
        pixelData.AsSpan(srcOffset, copyLen).CopyTo(stripData);

      // Apply predictor
      if (predictor == TiffPredictor.HorizontalDifferencing &&
          compression is not (TiffCompression.None or TiffCompression.PackBits) &&
          bitsPerSample == 8)
        HorizontalDifferencing.Apply(stripData, bytesPerRow, rowsInStrip, samplesPerPixel);

      chunks.Add(_CompressData(stripData, compression, zopfliIterations));
    }

    return chunks;
  }

  private static List<byte[]> _CompressTiles(byte[] pixelData, int width, int height,
    int samplesPerPixel, int bitsPerSample, int tileWidth, int tileHeight,
    TiffCompression compression, TiffPredictor predictor, int zopfliIterations) {
    var chunks = new List<byte[]>();
    var bytesPerPixel = (samplesPerPixel * bitsPerSample + 7) / 8;
    var bytesPerRow = width * bytesPerPixel;
    var tileBytesPerRow = tileWidth * bytesPerPixel;
    var tileSize = tileBytesPerRow * tileHeight;

    for (var ty = 0; ty < height; ty += tileHeight)
    for (var tx = 0; tx < width; tx += tileWidth) {
      var tileData = new byte[tileSize];

      var rowsInTile = Math.Min(tileHeight, height - ty);
      var colsInTile = Math.Min(tileWidth, width - tx);
      var bytesToCopy = colsInTile * bytesPerPixel;

      for (var r = 0; r < rowsInTile; ++r) {
        var srcOff = (ty + r) * bytesPerRow + tx * bytesPerPixel;
        var dstOff = r * tileBytesPerRow;
        if (srcOff + bytesToCopy <= pixelData.Length)
          pixelData.AsSpan(srcOff, bytesToCopy).CopyTo(tileData.AsSpan(dstOff));
      }

      if (predictor == TiffPredictor.HorizontalDifferencing &&
          compression is not (TiffCompression.None or TiffCompression.PackBits) &&
          bitsPerSample == 8)
        HorizontalDifferencing.Apply(tileData, tileBytesPerRow, tileHeight, samplesPerPixel);

      chunks.Add(_CompressData(tileData, compression, zopfliIterations));
    }

    return chunks;
  }

  private static byte[] _CompressData(byte[] data, TiffCompression compression, int zopfliIterations) => compression switch {
    TiffCompression.None => data,
    TiffCompression.PackBits => PackBitsCompressor.Compress(data),
    TiffCompression.Lzw => TiffLzwCompressor.Compress(data),
    TiffCompression.Deflate => TiffDeflateHelper.Compress(data),
    TiffCompression.DeflateUltra => TiffDeflateHelper.CompressZopfli(data, false, zopfliIterations),
    TiffCompression.DeflateHyper => TiffDeflateHelper.CompressZopfli(data, true, zopfliIterations),
    _ => data,
  };

  private static ushort _MapCompression(TiffCompression compression) => compression switch {
    TiffCompression.None => TiffConstants.CompressionNone,
    TiffCompression.PackBits => TiffConstants.CompressionPackBits,
    TiffCompression.Lzw => TiffConstants.CompressionLzw,
    TiffCompression.Deflate => TiffConstants.CompressionDeflate,
    TiffCompression.DeflateUltra => TiffConstants.CompressionDeflate,
    TiffCompression.DeflateHyper => TiffConstants.CompressionDeflate,
    _ => TiffConstants.CompressionNone,
  };

  internal static ushort _DeterminePhotometric(byte[]? colorMap, int samplesPerPixel) {
    if (colorMap != null)
      return TiffConstants.PhotometricPalette;
    if (samplesPerPixel == 1)
      return TiffConstants.PhotometricMinIsBlack;
    return TiffConstants.PhotometricRgb;
  }

  private static void _AddTag(List<(ushort Tag, ushort Type, uint Count, byte[] Value)> tags,
    ushort tag, ushort type, uint count, byte[] value)
    => tags.Add((tag, type, count, value));

  private static byte[] _UInt16Bytes(int value) {
    var bytes = new byte[2];
    BinaryPrimitives.WriteUInt16LittleEndian(bytes, (ushort)value);
    return bytes;
  }

  private static byte[] _UInt32Bytes(int value) {
    var bytes = new byte[4];
    BinaryPrimitives.WriteUInt32LittleEndian(bytes, (uint)value);
    return bytes;
  }

  private static void _WriteUInt16(MemoryStream ms, ushort value) {
    var bytes = new byte[2];
    BinaryPrimitives.WriteUInt16LittleEndian(bytes, value);
    ms.Write(bytes);
  }

  private static void _WriteUInt32(MemoryStream ms, uint value) {
    var bytes = new byte[4];
    BinaryPrimitives.WriteUInt32LittleEndian(bytes, value);
    ms.Write(bytes);
  }
}
