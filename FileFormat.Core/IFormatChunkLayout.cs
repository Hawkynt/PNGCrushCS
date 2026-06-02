using System;
using System.Collections.Generic;

namespace FileFormat.Core;

/// <summary>What role a chunk plays in the format. Generic categories that map onto each format's
/// own taxonomy — for PNG: IHDR=Header, PLTE=Palette, IDAT=PixelData, IEND=Footer, eXIf/tEXt/iCCP=Metadata,
/// the 8-byte 0x89 50 4E 47 ... preamble=Signature.</summary>
public enum ChunkKind {
  Unknown,
  /// <summary>File-identifying magic bytes (always at offset 0).</summary>
  Signature,
  /// <summary>Top-level header with format/dimension info — usually mandatory and position-locked.</summary>
  Header,
  /// <summary>Primary pixel/sample payload (IDAT, mdat, JPEG entropy-coded segment, ...).</summary>
  PixelData,
  /// <summary>Indexed-image palette table.</summary>
  Palette,
  /// <summary>Application metadata (EXIF, XMP, text comments, timestamps).</summary>
  Metadata,
  /// <summary>Colour profile data (ICC, sRGB, gamma, chromaticity).</summary>
  ColorProfile,
  /// <summary>End-of-file marker.</summary>
  Footer,
  /// <summary>Padding / filler bytes the caller may safely drop.</summary>
  Padding,
}

/// <summary>What a caller may do with a chunk during rewrite. Flags compose — a movable + removable
/// metadata chunk has <c>Movable | Removable</c>.</summary>
[Flags]
public enum ChunkMobility {
  /// <summary>Position-locked, cannot be moved or removed (signature, IHDR, IEND).</summary>
  Fixed = 0,
  /// <summary>May be relocated within the file as long as format-ordering constraints stay satisfied.</summary>
  Movable = 1 << 0,
  /// <summary>May be deleted entirely (typically ancillary metadata).</summary>
  Removable = 1 << 1,
  /// <summary>May be merged with adjacent same-name chunks (PNG split IDATs → one IDAT, JPEG split DRI, ...).</summary>
  Fusible = 1 << 2,
}

/// <summary>One contiguous structural region of a format-file. Returned by
/// <see cref="IFormatChunkLayout{TSelf}.EnumerateChunks(ReadOnlySpan{byte})"/>.</summary>
/// <param name="Name">Format-specific identifier: PNG 4cc ("IHDR"), JPEG marker name ("SOI", "APP1"), etc.
/// Special value "<c>SIGNATURE</c>" denotes the magic-byte preamble.</param>
/// <param name="Offset">Byte offset from the start of the file.</param>
/// <param name="Length">Total span length in bytes (includes any per-chunk header, length field, and CRC/footer).</param>
/// <param name="Kind">Semantic role — see <see cref="ChunkKind"/>.</param>
/// <param name="Mobility">What the rewriter is permitted to do with this chunk.</param>
/// <param name="Ordinal">0-based index disambiguating chunks that share a <see cref="Name"/> (e.g. multiple
/// PNG IDATs). Stable within one enumeration of the same byte stream.</param>
public readonly record struct ChunkSpan(
  string Name,
  long Offset,
  long Length,
  ChunkKind Kind,
  ChunkMobility Mobility,
  int Ordinal = 0
);

/// <summary>Implemented by formats that expose their byte-level internal structure for layout analysis,
/// visualisation, and rewriting tools. Read-only by itself — pair with <see cref="IFormatChunkRewriter{TSelf}"/>
/// for mutating operations.</summary>
public interface IFormatChunkLayout<TSelf> where TSelf : IFormatChunkLayout<TSelf> {

  /// <summary>Enumerates every top-level chunk in <paramref name="data"/> in file order. Implementations
  /// must not modify the input. Returns an empty sequence (not null, not throw) for malformed input.</summary>
  static abstract IEnumerable<ChunkSpan> EnumerateChunks(ReadOnlySpan<byte> data);
}
