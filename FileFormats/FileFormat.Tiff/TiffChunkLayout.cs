using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using FileFormat.Core;

namespace FileFormat.Tiff;

/// <summary>
/// TIFF byte-level layout enumeration. Implements <see cref="IFormatChunkLayout{TSelf}"/> for
/// <see cref="TiffFile"/> — read-only because TIFF rewriting requires patching offset pointers in
/// every IFD entry, which is a larger undertaking than the by-name / plan models that suit linear
/// chunk-based formats. Callers that need full rewrite should use a TIFF-specific tool.
/// </summary>
/// <remarks>
/// The enumeration emits one <see cref="ChunkSpan"/> for each top-level structural region:
/// <list type="bullet">
///   <item>The 8-byte header (endianness marker + magic + first-IFD offset).</item>
///   <item>Each IFD as a single span — the 2-byte tag count + 12 bytes/tag + 4-byte next-IFD pointer.</item>
///   <item>Each strip / tile of pixel data as a separate <see cref="ChunkKind.PixelData"/> span,
///   sourced from the <c>StripOffsets</c> + <c>StripByteCounts</c> tags (or tile equivalents).</item>
///   <item>Each large value (ICC profile, XMP, EXIF IFD, sub-IFDs) referenced from the IFD that lives
///   outside the IFD payload itself.</item>
/// </list>
/// All spans are reported as <see cref="ChunkMobility.Fixed"/> with <see cref="AllowedZones.PreData"/>
/// because moving any region in TIFF without recomputing IFD entry offsets would corrupt the file.
/// </remarks>
internal static class TiffChunkLayout {

  private const string _Header = "TiffHeader";

  public static IReadOnlyList<ChunkSpan> Enumerate(ReadOnlySpan<byte> data) {
    var result = new List<ChunkSpan>();
    if (data.Length < 8) return result;

    bool littleEndian;
    if (data[0] == 0x49 && data[1] == 0x49) littleEndian = true;
    else if (data[0] == 0x4D && data[1] == 0x4D) littleEndian = false;
    else return result;

    var magic = littleEndian ? BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(2, 2))
                             : BinaryPrimitives.ReadUInt16BigEndian(data.Slice(2, 2));
    if (magic != 42) return result;

    var firstIfdOffset = (int)(littleEndian
      ? BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(4, 4))
      : BinaryPrimitives.ReadUInt32BigEndian(data.Slice(4, 4)));

    result.Add(new ChunkSpan(_Header, 0, 8, ChunkKind.Signature, ChunkMobility.Fixed,
      Ordinal: 0, CurrentZone: ChunkZone.Signature, AllowedZones: AllowedZones.Signature));

    var ordinals = new Dictionary<string, int>(StringComparer.Ordinal) { [_Header] = 1 };
    int NextOrdinal(string name) {
      var n = ordinals.TryGetValue(name, out var v) ? v : 0;
      ordinals[name] = n + 1;
      return n;
    }

    var seenIfdOffsets = new HashSet<int>();
    var nextOffset = firstIfdOffset;
    var ifdIndex = 0;

    while (nextOffset != 0 && nextOffset + 2 <= data.Length && seenIfdOffsets.Add(nextOffset)) {
      var entryCount = (int)(littleEndian
        ? BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(nextOffset, 2))
        : BinaryPrimitives.ReadUInt16BigEndian(data.Slice(nextOffset, 2)));
      var ifdLen = 2 + entryCount * 12 + 4;
      if (nextOffset + ifdLen > data.Length) break;

      var ifdName = $"IFD{ifdIndex}";
      result.Add(new ChunkSpan(ifdName, nextOffset, ifdLen, ChunkKind.Header, ChunkMobility.Fixed,
        Ordinal: NextOrdinal(ifdName), CurrentZone: ChunkZone.Header, AllowedZones: AllowedZones.Header));

      // Walk entries to find strip/tile offsets + sizes and known metadata pointers.
      var stripOffsetsTagPos = -1;
      var stripByteCountsTagPos = -1;
      var tileOffsetsTagPos = -1;
      var tileByteCountsTagPos = -1;

      for (var i = 0; i < entryCount; ++i) {
        var entryPos = nextOffset + 2 + i * 12;
        var tag = (int)(littleEndian
          ? BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(entryPos, 2))
          : BinaryPrimitives.ReadUInt16BigEndian(data.Slice(entryPos, 2)));
        switch (tag) {
          case 273: stripOffsetsTagPos = entryPos; break;     // StripOffsets
          case 279: stripByteCountsTagPos = entryPos; break;  // StripByteCounts
          case 324: tileOffsetsTagPos = entryPos; break;      // TileOffsets
          case 325: tileByteCountsTagPos = entryPos; break;   // TileByteCounts
          case 34665: _ReportExternalIfd(data, entryPos, "ExifIFD", littleEndian, result, ordinals); break;
          case 34853: _ReportExternalIfd(data, entryPos, "GpsIFD", littleEndian, result, ordinals); break;
          case 34675: _ReportInlineBlob(data, entryPos, "ICC", littleEndian, result, ordinals); break;
          case 700:   _ReportInlineBlob(data, entryPos, "XMP", littleEndian, result, ordinals); break;
          case 33723: _ReportInlineBlob(data, entryPos, "IPTC", littleEndian, result, ordinals); break;
          case 34377: _ReportInlineBlob(data, entryPos, "PhotoshopIRB", littleEndian, result, ordinals); break;
        }
      }

      _ReportStripsOrTiles(data, stripOffsetsTagPos, stripByteCountsTagPos, "Strip", littleEndian, result, ordinals);
      _ReportStripsOrTiles(data, tileOffsetsTagPos, tileByteCountsTagPos, "Tile", littleEndian, result, ordinals);

      var nextPtrPos = nextOffset + 2 + entryCount * 12;
      nextOffset = (int)(littleEndian
        ? BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(nextPtrPos, 4))
        : BinaryPrimitives.ReadUInt32BigEndian(data.Slice(nextPtrPos, 4)));
      ifdIndex++;
    }

    return result;
  }

  private static void _ReportExternalIfd(ReadOnlySpan<byte> data, int entryPos, string name, bool le, List<ChunkSpan> result, Dictionary<string, int> ordinals) {
    if (entryPos + 12 > data.Length) return;
    // Value field for a long-pointer tag: bytes 8..11 hold the offset.
    var offset = (int)(le
      ? BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(entryPos + 8, 4))
      : BinaryPrimitives.ReadUInt32BigEndian(data.Slice(entryPos + 8, 4)));
    if (offset <= 0 || offset + 2 > data.Length) return;
    var ec = (int)(le
      ? BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(offset, 2))
      : BinaryPrimitives.ReadUInt16BigEndian(data.Slice(offset, 2)));
    var len = 2 + ec * 12 + 4;
    if (offset + len > data.Length) return;
    var n = ordinals.TryGetValue(name, out var v) ? v : 0;
    ordinals[name] = n + 1;
    result.Add(new ChunkSpan(name, offset, len, ChunkKind.Metadata, ChunkMobility.Fixed, n,
      CurrentZone: ChunkZone.PreData, AllowedZones: AllowedZones.PreData));
  }

  private static void _ReportInlineBlob(ReadOnlySpan<byte> data, int entryPos, string name, bool le, List<ChunkSpan> result, Dictionary<string, int> ordinals) {
    if (entryPos + 12 > data.Length) return;
    var count = (int)(le
      ? BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(entryPos + 4, 4))
      : BinaryPrimitives.ReadUInt32BigEndian(data.Slice(entryPos + 4, 4)));
    if (count <= 4) return; // value fits inline in the entry; nothing external to report
    var offset = (int)(le
      ? BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(entryPos + 8, 4))
      : BinaryPrimitives.ReadUInt32BigEndian(data.Slice(entryPos + 8, 4)));
    if (offset <= 0 || offset + count > data.Length) return;
    var n = ordinals.TryGetValue(name, out var v) ? v : 0;
    ordinals[name] = n + 1;
    result.Add(new ChunkSpan(name, offset, count,
      name == "ICC" ? ChunkKind.ColorProfile : ChunkKind.Metadata,
      ChunkMobility.Fixed, n,
      CurrentZone: ChunkZone.PreData, AllowedZones: AllowedZones.PreData));
  }

  private static void _ReportStripsOrTiles(ReadOnlySpan<byte> data, int offsetsTagPos, int sizesTagPos, string prefix, bool le, List<ChunkSpan> result, Dictionary<string, int> ordinals) {
    if (offsetsTagPos < 0 || sizesTagPos < 0) return;
    var offsetCount = (int)(le
      ? BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offsetsTagPos + 4, 4))
      : BinaryPrimitives.ReadUInt32BigEndian(data.Slice(offsetsTagPos + 4, 4)));
    var offsetType = (int)(le
      ? BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(offsetsTagPos + 2, 2))
      : BinaryPrimitives.ReadUInt16BigEndian(data.Slice(offsetsTagPos + 2, 2)));
    var sizeType = (int)(le
      ? BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(sizesTagPos + 2, 2))
      : BinaryPrimitives.ReadUInt16BigEndian(data.Slice(sizesTagPos + 2, 2)));

    var offsetsArrPos = _ResolveArrayPos(data, offsetsTagPos, offsetType, offsetCount, le);
    var sizesArrPos = _ResolveArrayPos(data, sizesTagPos, sizeType, offsetCount, le);
    if (offsetsArrPos < 0 || sizesArrPos < 0) return;

    var offEltSize = offsetType == 3 ? 2 : 4; // SHORT=3, LONG=4
    var sizeEltSize = sizeType == 3 ? 2 : 4;

    var name = $"{prefix}Data";
    for (var i = 0; i < offsetCount; ++i) {
      if (offsetsArrPos + (i + 1) * offEltSize > data.Length) break;
      if (sizesArrPos + (i + 1) * sizeEltSize > data.Length) break;
      var off = (int)_ReadUint(data, offsetsArrPos + i * offEltSize, offEltSize, le);
      var size = (int)_ReadUint(data, sizesArrPos + i * sizeEltSize, sizeEltSize, le);
      if (off <= 0 || size <= 0 || off + size > data.Length) continue;
      var n = ordinals.TryGetValue(name, out var v) ? v : 0;
      ordinals[name] = n + 1;
      result.Add(new ChunkSpan(name, off, size, ChunkKind.PixelData, ChunkMobility.Fixed, n,
        CurrentZone: ChunkZone.Data, AllowedZones: AllowedZones.Data));
    }
  }

  private static int _ResolveArrayPos(ReadOnlySpan<byte> data, int entryPos, int type, int count, bool le) {
    var eltSize = type == 3 ? 2 : 4;
    var inlineBytes = count * eltSize;
    if (inlineBytes <= 4) return entryPos + 8; // fits in the value-or-offset field
    var off = (int)(le
      ? BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(entryPos + 8, 4))
      : BinaryPrimitives.ReadUInt32BigEndian(data.Slice(entryPos + 8, 4)));
    if (off <= 0 || off + inlineBytes > data.Length) return -1;
    return off;
  }

  private static uint _ReadUint(ReadOnlySpan<byte> data, int pos, int size, bool le)
    => size == 2
      ? le ? BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(pos, 2)) : BinaryPrimitives.ReadUInt16BigEndian(data.Slice(pos, 2))
      : le ? BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(pos, 4)) : BinaryPrimitives.ReadUInt32BigEndian(data.Slice(pos, 4));
}
