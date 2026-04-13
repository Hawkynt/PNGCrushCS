using System;
using System.Collections.Generic;
using System.IO;

namespace FileFormat.Xcf;

/// <summary>Reads XCF files from bytes, streams, or file paths.</summary>
public static class XcfReader {

  private const int TILE_SIZE = 64;
  private static readonly byte[] _MAGIC = "gimp xcf "u8.ToArray();

  // Property types
  private const uint PROP_END = 0;
  private const uint PROP_COLORMAP = 1;
  private const uint PROP_COMPRESSION = 17;
  private const uint PROP_OFFSETS = 15;
  private const uint PROP_OPACITY = 6;
  private const uint PROP_VISIBLE = 8;
  private const uint PROP_ACTIVE_LAYER = 2;

  // Layer types
  private const uint LAYER_RGB = 0;
  private const uint LAYER_RGBA = 1;
  private const uint LAYER_GRAY = 2;
  private const uint LAYER_GRAYA = 3;
  private const uint LAYER_INDEXED = 4;
  private const uint LAYER_INDEXEDA = 5;

  public static XcfFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("XCF file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static XcfFile FromStream(Stream stream) {
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

  public static XcfFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < 14)
      throw new InvalidDataException("Data too small for a valid XCF file.");

    var span = data;

    // Validate magic: "gimp xcf "
    for (var i = 0; i < _MAGIC.Length; ++i)
      if (span[i] != _MAGIC[i])
        throw new InvalidDataException("Invalid XCF magic.");

    // Parse version string (after "gimp xcf ")
    var versionStart = _MAGIC.Length;
    var versionEnd = versionStart;
    while (versionEnd < data.Length && data[versionEnd] != 0)
      ++versionEnd;

    var versionStr = System.Text.Encoding.ASCII.GetString(data.Slice(versionStart, versionEnd - versionStart));
    var version = _ParseVersion(versionStr);
    var useWideOffsets = version >= 11;

    var offset = versionEnd + 1; // skip null terminator

    // Read canvas dimensions and color mode
    if (offset + 12 > data.Length)
      throw new InvalidDataException("XCF file truncated before canvas properties.");

    var width = (int)_ReadUInt32BE(span[offset..]);
    var height = (int)_ReadUInt32BE(span[(offset + 4)..]);
    var colorMode = (XcfColorMode)_ReadUInt32BE(span[(offset + 8)..]);
    offset += 12;

    // Parse image properties
    var compression = XcfCompression.None;
    byte[]? palette = null;
    offset = _ParseProperties(data, offset, ref compression, ref palette);

    // Read first layer pointer
    uint layerOffset;
    if (useWideOffsets) {
      if (offset + 8 > data.Length)
        throw new InvalidDataException("XCF file truncated at layer pointer.");
      layerOffset = (uint)_ReadUInt64BE(span[offset..]);
      offset += 8;
    } else {
      if (offset + 4 > data.Length)
        throw new InvalidDataException("XCF file truncated at layer pointer.");
      layerOffset = _ReadUInt32BE(span[offset..]);
      offset += 4;
    }

    // Extract pixel data from first layer
    byte[] pixelData;
    if (layerOffset == 0 || layerOffset >= data.Length)
      pixelData = [];
    else
      pixelData = _ReadLayer(data, (int)layerOffset, compression, useWideOffsets, width, height);

    return new XcfFile {
      Width = width,
      Height = height,
      ColorMode = colorMode,
      Version = version,
      PixelData = pixelData,
      Palette = palette
    };
  
  }

  public static XcfFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }

  private static int _ParseVersion(string versionStr) => versionStr switch {
    "file" => 0,
    "v001" => 1,
    "v002" => 2,
    "v003" => 3,
    "v004" => 4,
    "v005" => 5,
    "v006" => 6,
    "v007" => 7,
    "v008" => 8,
    "v009" => 9,
    "v010" => 10,
    "v011" => 11,
    "v012" => 12,
    "v013" => 13,
    "v014" => 14,
    _ => 0
  };

  private static int _ParseProperties(ReadOnlySpan<byte> data, int offset, ref XcfCompression compression, ref byte[]? palette) {
    while (offset + 8 <= data.Length) {
      var propType = _ReadUInt32BE(data[offset..]);
      var propSize = (int)_ReadUInt32BE(data[(offset + 4)..]);
      offset += 8;

      if (propType == PROP_END)
        break;

      if (offset + propSize > data.Length)
        break;

      switch (propType) {
        case PROP_COMPRESSION:
          if (propSize >= 1)
            compression = (XcfCompression)data[offset];
          break;
        case PROP_COLORMAP:
          if (propSize >= 4) {
            var colorCount = (int)_ReadUInt32BE(data[offset..]);
            var paletteDataSize = colorCount * 3;
            if (offset + 4 + paletteDataSize <= data.Length) {
              palette = new byte[paletteDataSize];
              data.Slice(offset + 4, paletteDataSize).CopyTo(palette);
            }
          }
          break;
      }

      offset += propSize;
    }

    return offset;
  }

  private static byte[] _ReadLayer(ReadOnlySpan<byte> data, int layerStart, XcfCompression compression, bool useWideOffsets, int canvasWidth, int canvasHeight) {
    var offset = layerStart;

    if (offset + 12 > data.Length)
      return [];

    var layerWidth = (int)_ReadUInt32BE(data[offset..]);
    var layerHeight = (int)_ReadUInt32BE(data[(offset + 4)..]);
    var layerType = _ReadUInt32BE(data[(offset + 8)..]);
    offset += 12;

    var bpp = _LayerTypeToBpp(layerType);

    // Skip name (null-terminated string prefixed by uint32 length)
    if (offset + 4 > data.Length)
      return [];
    var nameLen = (int)_ReadUInt32BE(data[offset..]);
    offset += 4 + nameLen;

    // Parse layer properties (skip all)
    while (offset + 8 <= data.Length) {
      var propType = _ReadUInt32BE(data[offset..]);
      var propSize = (int)_ReadUInt32BE(data[(offset + 4)..]);
      offset += 8;
      if (propType == PROP_END)
        break;
      offset += propSize;
    }

    // Read hierarchy pointer
    uint hierarchyOffset;
    if (useWideOffsets) {
      if (offset + 8 > data.Length)
        return [];
      hierarchyOffset = (uint)_ReadUInt64BE(data[offset..]);
    } else {
      if (offset + 4 > data.Length)
        return [];
      hierarchyOffset = _ReadUInt32BE(data[offset..]);
    }

    if (hierarchyOffset == 0 || hierarchyOffset >= data.Length)
      return [];

    return _ReadHierarchy(data, (int)hierarchyOffset, bpp, compression, useWideOffsets, layerWidth, layerHeight);
  }

  private static byte[] _ReadHierarchy(ReadOnlySpan<byte> data, int hierarchyStart, int bpp, XcfCompression compression, bool useWideOffsets, int width, int height) {
    var offset = hierarchyStart;

    if (offset + 12 > data.Length)
      return [];

    // hierarchy: width, height, bpp
    offset += 12; // skip width, height, bpp (already known)

    // Read first level pointer (full resolution)
    uint levelOffset;
    if (useWideOffsets) {
      if (offset + 8 > data.Length)
        return [];
      levelOffset = (uint)_ReadUInt64BE(data[offset..]);
    } else {
      if (offset + 4 > data.Length)
        return [];
      levelOffset = _ReadUInt32BE(data[offset..]);
    }

    if (levelOffset == 0 || levelOffset >= data.Length)
      return [];

    return _ReadLevel(data, (int)levelOffset, bpp, compression, useWideOffsets, width, height);
  }

  private static byte[] _ReadLevel(ReadOnlySpan<byte> data, int levelStart, int bpp, XcfCompression compression, bool useWideOffsets, int width, int height) {
    var offset = levelStart;

    if (offset + 8 > data.Length)
      return [];

    // level: width, height
    offset += 8;

    // Read tile offsets
    var tileOffsets = new List<uint>();
    while (true) {
      uint tileOffset;
      if (useWideOffsets) {
        if (offset + 8 > data.Length)
          break;
        tileOffset = (uint)_ReadUInt64BE(data[offset..]);
        offset += 8;
      } else {
        if (offset + 4 > data.Length)
          break;
        tileOffset = _ReadUInt32BE(data[offset..]);
        offset += 4;
      }

      if (tileOffset == 0)
        break;
      tileOffsets.Add(tileOffset);
    }

    // Assemble tiles into full pixel data
    var result = new byte[width * height * bpp];
    var tilesPerRow = (width + TILE_SIZE - 1) / TILE_SIZE;
    var tilesPerCol = (height + TILE_SIZE - 1) / TILE_SIZE;

    for (var tileIndex = 0; tileIndex < tileOffsets.Count && tileIndex < tilesPerRow * tilesPerCol; ++tileIndex) {
      var tileX = tileIndex % tilesPerRow;
      var tileY = tileIndex / tilesPerRow;
      var tileW = Math.Min(TILE_SIZE, width - tileX * TILE_SIZE);
      var tileH = Math.Min(TILE_SIZE, height - tileY * TILE_SIZE);
      var tilePixelCount = tileW * tileH;
      var tileByteCount = tilePixelCount * bpp;

      var tileStart = (int)tileOffsets[tileIndex];
      if (tileStart >= data.Length)
        continue;

      // Determine tile data end (next tile offset or end of data)
      var tileEnd = data.Length;
      if (tileIndex + 1 < tileOffsets.Count)
        tileEnd = (int)tileOffsets[tileIndex + 1];

      var tileDataLen = tileEnd - tileStart;
      if (tileDataLen <= 0)
        continue;

      var tileCompressed = new byte[Math.Min(tileDataLen, data.Length - tileStart)];
      data.Slice(tileStart, tileCompressed.Length).CopyTo(tileCompressed);

      byte[] tilePixels;
      switch (compression) {
        case XcfCompression.Rle:
          tilePixels = XcfTileDecoder.DecodeRle(tileCompressed, bpp, tileW, tileH);
          break;
        case XcfCompression.None:
          tilePixels = XcfTileDecoder.DecodeUncompressed(tileCompressed, bpp, tileW, tileH);
          break;
        default:
          continue;
      }

      // Copy tile pixels into result at the correct position
      for (var row = 0; row < tileH; ++row) {
        var srcRow = row * tileW * bpp;
        var dstRow = ((tileY * TILE_SIZE + row) * width + tileX * TILE_SIZE) * bpp;
        if (dstRow + tileW * bpp > result.Length)
          break;
        tilePixels.AsSpan(srcRow, tileW * bpp).CopyTo(result.AsSpan(dstRow));
      }
    }

    return result;
  }

  private static int _LayerTypeToBpp(uint layerType) => layerType switch {
    LAYER_RGB => 3,
    LAYER_RGBA => 4,
    LAYER_GRAY => 1,
    LAYER_GRAYA => 2,
    LAYER_INDEXED => 1,
    LAYER_INDEXEDA => 2,
    _ => 4
  };

  private static uint _ReadUInt32BE(ReadOnlySpan<byte> data)
    => (uint)(data[0] << 24 | data[1] << 16 | data[2] << 8 | data[3]);

  private static ulong _ReadUInt64BE(ReadOnlySpan<byte> data)
    => (ulong)data[0] << 56 | (ulong)data[1] << 48 | (ulong)data[2] << 40 | (ulong)data[3] << 32
     | (ulong)data[4] << 24 | (ulong)data[5] << 16 | (ulong)data[6] << 8 | data[7];
}
