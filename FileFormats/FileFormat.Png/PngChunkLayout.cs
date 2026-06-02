using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Text;
using FileFormat.Core;

namespace FileFormat.Png;

/// <summary>
/// PNG byte-level layout enumeration and rewrite. Implements <see cref="IFormatChunkLayout{TSelf}"/> and
/// <see cref="IFormatChunkRewriter{TSelf}"/> for <see cref="PngFile"/>.
/// </summary>
/// <remarks>
/// Chunk byte spans (4-byte length + 4-byte type + data + 4-byte CRC) are copied intact when moving,
/// so per-chunk CRCs need no recomputation. <see cref="ChunkPlacement.Fuse"/> on IDAT concatenates all
/// IDAT data segments into a single IDAT chunk and recomputes the CRC over (type + merged data).
/// <para/>
/// Ordering constraints baked in (PNG spec):
/// <list type="bullet">
///   <item>Signature is always at offset 0.</item>
///   <item>IHDR is always the first chunk.</item>
///   <item>PLTE, tRNS, bKGD, hIST, sPLT must appear before any IDAT.</item>
///   <item>IEND is always the last chunk.</item>
/// </list>
/// Rules whose placement would violate the spec are silently dropped.
/// </remarks>
internal static class PngChunkLayout {

  private const string _Signature = "SIGNATURE";

  private static readonly byte[] _PngSignatureBytes = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

  /// <summary>Critical chunks whose position is mandated by the PNG spec.</summary>
  private static readonly HashSet<string> _PreIdatOnlyCritical = ["PLTE"];

  /// <summary>Ancillary chunks that the PNG spec requires before IDAT (mostly palette-dependent).</summary>
  private static readonly HashSet<string> _PreIdatOnlyAncillary = ["tRNS", "bKGD", "hIST", "sPLT"];

  public static IReadOnlyList<ChunkSpan> Enumerate(ReadOnlySpan<byte> data) {
    var result = new List<ChunkSpan>();
    if (data.Length < _PngSignatureBytes.Length) return result;
    for (var i = 0; i < _PngSignatureBytes.Length; ++i)
      if (data[i] != _PngSignatureBytes[i]) return result;

    result.Add(new ChunkSpan(_Signature, 0, _PngSignatureBytes.Length, ChunkKind.Signature, ChunkMobility.Fixed));

    var ordinals = new Dictionary<string, int>(StringComparer.Ordinal);
    var pos = _PngSignatureBytes.Length;

    while (pos + 12 <= data.Length) {
      var length = BinaryPrimitives.ReadUInt32BigEndian(data.Slice(pos, 4));
      if (length > int.MaxValue) break;
      var type = Encoding.ASCII.GetString(data.Slice(pos + 4, 4));
      var total = 12 + (long)length; // 4 length + 4 type + data + 4 CRC
      if (pos + total > data.Length) break;

      var ordinal = ordinals.TryGetValue(type, out var n) ? n : 0;
      ordinals[type] = ordinal + 1;

      var (kind, mobility) = _Classify(type);
      result.Add(new ChunkSpan(type, pos, total, kind, mobility, ordinal));

      pos += (int)total;
      if (type == "IEND") break;
    }

    return result;
  }

  public static byte[] Rewrite(ReadOnlySpan<byte> data, IReadOnlyList<ChunkRewriteRule> rules) {
    var chunks = Enumerate(data);
    if (chunks.Count == 0) return data.ToArray();

    // Bucket every non-signature, non-Footer chunk into pre/post groups. Header (IHDR) keeps its
    // identity; signature is implicit. Original bucket assignment matches existing position
    // relative to the first IDAT.
    var firstIdatIdx = -1;
    for (var i = 0; i < chunks.Count; ++i)
      if (chunks[i].Name == "IDAT") { firstIdatIdx = i; break; }

    var ihdr = default(ChunkSpan?);
    var iend = default(ChunkSpan?);
    var preData = new List<ChunkSpan>();
    var idats = new List<ChunkSpan>();
    var postData = new List<ChunkSpan>();

    for (var i = 0; i < chunks.Count; ++i) {
      var ch = chunks[i];
      if (ch.Name == _Signature) continue;
      if (ch.Name == "IHDR") { ihdr = ch; continue; }
      if (ch.Name == "IEND") { iend = ch; continue; }
      if (ch.Name == "IDAT") { idats.Add(ch); continue; }
      if (firstIdatIdx >= 0 && i < firstIdatIdx) preData.Add(ch);
      else if (firstIdatIdx >= 0) postData.Add(ch);
      else preData.Add(ch);
    }

    // Apply rules. Each rule targets a chunk name; we move every same-named chunk together.
    foreach (var rule in rules) {
      switch (rule.Placement) {
        case ChunkPlacement.Remove:
          _RemoveAllNamed(preData, rule.Name, requireRemovable: true);
          _RemoveAllNamed(postData, rule.Name, requireRemovable: true);
          if (rule.Name == "IDAT") { /* never honoured — IDAT cannot be removed entirely */ }
          break;
        case ChunkPlacement.BeforeData:
          if (rule.Name == "IDAT" || rule.Name == "IHDR" || rule.Name == "IEND") break;
          _MoveAllNamed(postData, preData, rule.Name, requireMovable: true, mustStayBeforeIdat: true);
          break;
        case ChunkPlacement.AfterData:
          if (rule.Name == "IDAT" || rule.Name == "IHDR" || rule.Name == "IEND") break;
          // Ancillary chunks PNG spec pins before IDAT (PLTE, tRNS, bKGD, hIST, sPLT) cannot move after.
          if (_PreIdatOnlyCritical.Contains(rule.Name) || _PreIdatOnlyAncillary.Contains(rule.Name)) break;
          _MoveAllNamed(preData, postData, rule.Name, requireMovable: true, mustStayBeforeIdat: false);
          break;
        case ChunkPlacement.Fuse:
          if (rule.Name != "IDAT") break; // only IDAT fusion is supported today
          // Fuse handled below in emission so we have access to the original byte source.
          break;
        case ChunkPlacement.Keep:
        default:
          break;
      }
    }

    var fuseIdats = false;
    foreach (var rule in rules)
      if (rule is { Name: "IDAT", Placement: ChunkPlacement.Fuse }) { fuseIdats = true; break; }

    // Emit.
    using var output = new System.IO.MemoryStream(data.Length);
    output.Write(_PngSignatureBytes, 0, _PngSignatureBytes.Length);
    if (ihdr is { } h) _CopyChunkBytes(data, h, output);
    foreach (var ch in preData) _CopyChunkBytes(data, ch, output);
    if (fuseIdats && idats.Count > 1) _WriteFusedIdat(data, idats, output);
    else foreach (var ch in idats) _CopyChunkBytes(data, ch, output);
    foreach (var ch in postData) _CopyChunkBytes(data, ch, output);
    if (iend is { } e) _CopyChunkBytes(data, e, output);
    else _WriteEmptyIend(output);

    return output.ToArray();
  }

  private static void _RemoveAllNamed(List<ChunkSpan> bucket, string name, bool requireRemovable) {
    for (var i = bucket.Count - 1; i >= 0; --i) {
      var ch = bucket[i];
      if (ch.Name != name) continue;
      if (requireRemovable && (ch.Mobility & ChunkMobility.Removable) == 0) continue;
      bucket.RemoveAt(i);
    }
  }

  private static void _MoveAllNamed(List<ChunkSpan> source, List<ChunkSpan> dest, string name, bool requireMovable, bool mustStayBeforeIdat) {
    for (var i = source.Count - 1; i >= 0; --i) {
      var ch = source[i];
      if (ch.Name != name) continue;
      if (requireMovable && (ch.Mobility & ChunkMobility.Movable) == 0) continue;
      source.RemoveAt(i);
      dest.Add(ch);
    }
  }

  private static void _CopyChunkBytes(ReadOnlySpan<byte> data, ChunkSpan ch, System.IO.MemoryStream output) {
    var slice = data.Slice((int)ch.Offset, (int)ch.Length);
    output.Write(slice);
  }

  private static void _WriteFusedIdat(ReadOnlySpan<byte> data, List<ChunkSpan> idats, System.IO.MemoryStream output) {
    long totalDataLen = 0;
    foreach (var ch in idats) totalDataLen += ch.Length - 12; // strip length + type + crc
    if (totalDataLen > int.MaxValue) {
      // Falls back to per-chunk emission if combined data exceeds PNG's 32-bit chunk length cap.
      foreach (var ch in idats) _CopyChunkBytes(data, ch, output);
      return;
    }

    var fused = new byte[totalDataLen];
    var pos = 0;
    foreach (var ch in idats) {
      var dataLen = (int)(ch.Length - 12);
      data.Slice((int)ch.Offset + 8, dataLen).CopyTo(fused.AsSpan(pos));
      pos += dataLen;
    }

    Span<byte> lengthBuf = stackalloc byte[4];
    BinaryPrimitives.WriteUInt32BigEndian(lengthBuf, (uint)fused.Length);
    output.Write(lengthBuf);
    var typeBytes = "IDAT"u8;
    output.Write(typeBytes);
    output.Write(fused);
    Span<byte> crcBuf = stackalloc byte[4];
    var crc = _Crc32Type(typeBytes, fused);
    BinaryPrimitives.WriteUInt32BigEndian(crcBuf, crc);
    output.Write(crcBuf);
  }

  private static void _WriteEmptyIend(System.IO.MemoryStream output) {
    output.Write(stackalloc byte[] { 0, 0, 0, 0, (byte)'I', (byte)'E', (byte)'N', (byte)'D' });
    // CRC for empty IEND: known constant 0xAE426082.
    output.Write(stackalloc byte[] { 0xAE, 0x42, 0x60, 0x82 });
  }

  // ---- chunk classification ----

  private static (ChunkKind Kind, ChunkMobility Mobility) _Classify(string type) => type switch {
    "IHDR" => (ChunkKind.Header, ChunkMobility.Fixed),
    "PLTE" => (ChunkKind.Palette, ChunkMobility.Movable),
    "IDAT" => (ChunkKind.PixelData, ChunkMobility.Movable | ChunkMobility.Fusible),
    "IEND" => (ChunkKind.Footer, ChunkMobility.Fixed),
    "iCCP" or "sRGB" or "gAMA" or "cHRM" or "sBIT" or "pHYs" or "tIME"
      => (ChunkKind.ColorProfile, ChunkMobility.Movable | ChunkMobility.Removable),
    "tRNS" or "bKGD" or "hIST" or "sPLT"
      => (ChunkKind.Metadata, ChunkMobility.Movable),
    "tEXt" or "zTXt" or "iTXt" or "eXIf"
      => (ChunkKind.Metadata, ChunkMobility.Movable | ChunkMobility.Removable),
    _ when type.Length == 4 && char.IsUpper(type[0])
      => (ChunkKind.Unknown, ChunkMobility.Fixed),
    _ when type.Length == 4
      => (ChunkKind.Metadata, ChunkMobility.Movable | ChunkMobility.Removable),
    _ => (ChunkKind.Unknown, ChunkMobility.Fixed),
  };

  // ---- CRC-32 (PNG / ISO 3309) ----

  private static readonly uint[] _Crc32Table = _BuildCrc32Table();

  private static uint[] _BuildCrc32Table() {
    var t = new uint[256];
    for (uint i = 0; i < 256; i++) {
      var c = i;
      for (var k = 0; k < 8; k++)
        c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
      t[i] = c;
    }
    return t;
  }

  private static uint _Crc32Type(ReadOnlySpan<byte> typeBytes, ReadOnlySpan<byte> data) {
    var c = 0xFFFFFFFFu;
    foreach (var b in typeBytes) c = _Crc32Table[(c ^ b) & 0xFF] ^ (c >> 8);
    foreach (var b in data) c = _Crc32Table[(c ^ b) & 0xFF] ^ (c >> 8);
    return c ^ 0xFFFFFFFFu;
  }
}
