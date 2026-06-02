using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using FileFormat.Core;

namespace FileFormat.Bmp;

/// <summary>BMP byte-level layout enumeration. Read-only — BMP has too little movable structure to
/// warrant a rewriter: file-header (14 bytes) + DIB header + optional palette + pixel data, all with
/// the file-header carrying an explicit pixel-data offset that would need patching for any rearrangement.</summary>
internal static class BmpChunkLayout {

  public static IReadOnlyList<ChunkSpan> Enumerate(ReadOnlySpan<byte> data) {
    var result = new List<ChunkSpan>();
    if (data.Length < 14 || data[0] != 0x42 || data[1] != 0x4D) return result;

    var fileSize = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(2, 4));
    var pixelDataOffset = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(10, 4));

    // 1. File header (always 14 bytes, always at offset 0).
    result.Add(new ChunkSpan("FileHeader", 0, 14, ChunkKind.Signature, ChunkMobility.Fixed,
      CurrentZone: ChunkZone.Signature, AllowedZones: AllowedZones.Signature));

    // 2. DIB header — its own size is the first uint32 at offset 14.
    if (data.Length < 18) return result;
    var dibSize = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(14, 4));
    if (dibSize < 12 || 14 + dibSize > data.Length) return result;
    result.Add(new ChunkSpan("DibHeader", 14, dibSize, ChunkKind.Header, ChunkMobility.Fixed,
      CurrentZone: ChunkZone.Header, AllowedZones: AllowedZones.Header));

    // 3. Optional palette — between DIB header and pixel-data offset.
    var paletteStart = 14 + dibSize;
    if (pixelDataOffset > paletteStart && pixelDataOffset <= data.Length) {
      result.Add(new ChunkSpan("Palette", paletteStart, pixelDataOffset - paletteStart,
        ChunkKind.Palette, ChunkMobility.Fixed,
        CurrentZone: ChunkZone.PreData, AllowedZones: AllowedZones.PreData));
    }

    // 4. Pixel data — runs from pixelDataOffset to fileSize (or EOF).
    if (pixelDataOffset > 0 && pixelDataOffset < data.Length) {
      var pixelEnd = Math.Min(fileSize > 0 ? fileSize : data.Length, data.Length);
      if (pixelEnd > pixelDataOffset)
        result.Add(new ChunkSpan("PixelData", pixelDataOffset, pixelEnd - pixelDataOffset,
          ChunkKind.PixelData, ChunkMobility.Fixed,
          CurrentZone: ChunkZone.Data, AllowedZones: AllowedZones.Data));
    }

    return result;
  }
}
