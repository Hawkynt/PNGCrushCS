using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
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

    result.Add(new ChunkSpan(
      _Signature, 0, _PngSignatureBytes.Length, ChunkKind.Signature, ChunkMobility.Fixed,
      Ordinal: 0, CurrentZone: ChunkZone.Signature, AllowedZones: AllowedZones.Signature));

    var ordinals = new Dictionary<string, int>(StringComparer.Ordinal);
    var pos = _PngSignatureBytes.Length;

    // First pass: parse raw chunks
    var raw = new List<(string Type, int Offset, int Total, int Ordinal, ChunkKind Kind, ChunkMobility Mobility, AllowedZones Allowed)>();
    while (pos + 12 <= data.Length) {
      var length = BinaryPrimitives.ReadUInt32BigEndian(data.Slice(pos, 4));
      if (length > int.MaxValue) break;
      var type = Encoding.ASCII.GetString(data.Slice(pos + 4, 4));
      var total = 12 + (long)length;
      if (pos + total > data.Length) break;

      var ordinal = ordinals.TryGetValue(type, out var n) ? n : 0;
      ordinals[type] = ordinal + 1;

      var (kind, mobility, allowed) = _Classify(type);
      raw.Add((type, pos, (int)total, ordinal, kind, mobility, allowed));

      pos += (int)total;
      if (type == "IEND") break;
    }

    // Second pass: assign CurrentZone based on position relative to the first IDAT.
    var firstIdatIdx = -1;
    for (var i = 0; i < raw.Count; ++i)
      if (raw[i].Type == "IDAT") { firstIdatIdx = i; break; }

    for (var i = 0; i < raw.Count; ++i) {
      var r = raw[i];
      var zone = r.Type switch {
        "IHDR" => ChunkZone.Header,
        "IEND" => ChunkZone.Footer,
        "IDAT" => ChunkZone.Data,
        _ when firstIdatIdx >= 0 && i < firstIdatIdx => ChunkZone.PreData,
        _ when firstIdatIdx >= 0 => ChunkZone.PostData,
        _ => ChunkZone.PreData,
      };
      result.Add(new ChunkSpan(r.Type, r.Offset, r.Total, r.Kind, r.Mobility, r.Ordinal, zone, r.Allowed));
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

  // PNG-spec-accurate placement rules:
  // - IHDR / IEND: position-locked
  // - IDAT: only in Data zone (contiguous run)
  // - PLTE, tRNS, bKGD, hIST, sPLT: must precede IDAT (PreData only)
  // - Colour-profile / display hints (cHRM, gAMA, iCCP, sBIT, sRGB, pHYs): must precede IDAT (PreData only)
  // - Text + timestamp + EXIF (tEXt, zTXt, iTXt, tIME, eXIf): no ordering constraint → PreData | PostData
  // - Unknown ancillary: lenient → PreData | PostData
  // - Unknown critical: strict → current zone only (we mark Fixed and don't touch)
  private static (ChunkKind Kind, ChunkMobility Mobility, AllowedZones Allowed) _Classify(string type) => type switch {
    "IHDR" => (ChunkKind.Header, ChunkMobility.Fixed, AllowedZones.Header),
    "PLTE" => (ChunkKind.Palette, ChunkMobility.Movable, AllowedZones.PreData),
    "IDAT" => (ChunkKind.PixelData, ChunkMobility.Movable | ChunkMobility.Fusible, AllowedZones.Data),
    "IEND" => (ChunkKind.Footer, ChunkMobility.Fixed, AllowedZones.Footer),
    "iCCP" or "sRGB" or "gAMA" or "cHRM" or "sBIT" or "pHYs"
      => (ChunkKind.ColorProfile, ChunkMobility.Movable | ChunkMobility.Removable, AllowedZones.PreData),
    "tRNS" or "bKGD" or "hIST" or "sPLT"
      => (ChunkKind.Metadata, ChunkMobility.Movable, AllowedZones.PreData),
    "tEXt" or "zTXt" or "iTXt" or "tIME" or "eXIf"
      => (ChunkKind.Metadata, ChunkMobility.Movable | ChunkMobility.Removable, AllowedZones.PreData | AllowedZones.PostData),
    _ when type.Length == 4 && char.IsUpper(type[0])
      => (ChunkKind.Unknown, ChunkMobility.Fixed, AllowedZones.PreData),
    _ when type.Length == 4
      => (ChunkKind.Metadata, ChunkMobility.Movable | ChunkMobility.Removable, AllowedZones.PreData | AllowedZones.PostData),
    _ => (ChunkKind.Unknown, ChunkMobility.Fixed, AllowedZones.None),
  };

  // ---- plan-based rewrite ----

  public static ChunkRewriteResult ApplyPlan(ReadOnlySpan<byte> data, ChunkRewritePlan plan) {
    var failures = new List<ChunkRewriteFailure>();
    var chunks = Enumerate(data);
    if (chunks.Count == 0) {
      failures.Add(new ChunkRewriteFailure("Validate", "(file)", 0, "Not a valid PNG."));
      return new ChunkRewriteResult { Failures = failures };
    }

    // Build a map of (Name, Ordinal) → ChunkSpan for lookup.
    var byRef = new Dictionary<ChunkReference, ChunkSpan>();
    foreach (var ch in chunks)
      byRef[new ChunkReference(ch.Name, ch.Ordinal)] = ch;

    // Validate placements.
    var requestedZone = new Dictionary<ChunkReference, (ChunkZone Zone, int? Order)>();
    foreach (var p in plan.Placements) {
      if (!byRef.TryGetValue(p.Chunk, out var ch)) {
        failures.Add(new ChunkRewriteFailure("Place", p.Chunk.Name, p.Chunk.Ordinal,
          $"No chunk with name '{p.Chunk.Name}' and ordinal {p.Chunk.Ordinal} found."));
        continue;
      }
      var asFlag = _ZoneToFlag(p.TargetZone);
      if ((ch.AllowedZones & asFlag) == 0) {
        failures.Add(new ChunkRewriteFailure("Place", p.Chunk.Name, p.Chunk.Ordinal,
          $"Chunk '{p.Chunk.Name}' may not occupy zone {p.TargetZone}. Allowed: {ch.AllowedZones}."));
        continue;
      }
      requestedZone[p.Chunk] = (p.TargetZone, p.OrderInZone);
    }

    // Validate removals.
    var toRemove = new HashSet<ChunkReference>();
    foreach (var r in plan.Remove) {
      if (!byRef.TryGetValue(r, out var ch)) {
        failures.Add(new ChunkRewriteFailure("Remove", r.Name, r.Ordinal,
          $"No chunk with name '{r.Name}' and ordinal {r.Ordinal} found."));
        continue;
      }
      if ((ch.Mobility & ChunkMobility.Removable) == 0) {
        failures.Add(new ChunkRewriteFailure("Remove", r.Name, r.Ordinal,
          $"Chunk '{r.Name}' is not removable (mobility: {ch.Mobility})."));
        continue;
      }
      toRemove.Add(r);
    }

    // Validate fusions.
    var fuseNames = new HashSet<string>(StringComparer.Ordinal);
    foreach (var name in plan.Fuse) {
      var instances = chunks.Where(c => c.Name == name).ToList();
      if (instances.Count == 0) {
        failures.Add(new ChunkRewriteFailure("Fuse", name, 0,
          $"No chunks named '{name}' found."));
        continue;
      }
      if (instances.Any(c => (c.Mobility & ChunkMobility.Fusible) == 0)) {
        failures.Add(new ChunkRewriteFailure("Fuse", name, 0,
          $"Chunk '{name}' is not fusible (mobility: {instances[0].Mobility})."));
        continue;
      }
      fuseNames.Add(name);
    }

    if (failures.Count > 0)
      return new ChunkRewriteResult { Failures = failures };

    // Build buckets per zone using effective placements (requested or current).
    var preData = new List<(ChunkSpan Ch, int? Order, int OriginalIdx)>();
    var postData = new List<(ChunkSpan Ch, int? Order, int OriginalIdx)>();
    var ihdr = default(ChunkSpan?);
    var iend = default(ChunkSpan?);
    var idats = new List<ChunkSpan>();

    for (var i = 0; i < chunks.Count; ++i) {
      var ch = chunks[i];
      if (ch.Name == _Signature) continue;
      if (toRemove.Contains(new ChunkReference(ch.Name, ch.Ordinal))) continue;
      var key = new ChunkReference(ch.Name, ch.Ordinal);
      var (effectiveZone, order) = requestedZone.TryGetValue(key, out var pz) ? pz : (ch.CurrentZone, (int?)null);

      switch (effectiveZone) {
        case ChunkZone.Header: ihdr = ch; break;
        case ChunkZone.Footer: iend = ch; break;
        case ChunkZone.Data: idats.Add(ch); break;
        case ChunkZone.PreData: preData.Add((ch, order, i)); break;
        case ChunkZone.PostData: postData.Add((ch, order, i)); break;
        case ChunkZone.Signature: break; // signature stays fixed
      }
    }

    if (ihdr == null) {
      failures.Add(new ChunkRewriteFailure("Validate", "IHDR", 0, "IHDR missing after applying plan."));
      return new ChunkRewriteResult { Failures = failures };
    }

    // Sort intra-zone: by Order (null last), then by original index.
    static int Compare((ChunkSpan Ch, int? Order, int OriginalIdx) a, (ChunkSpan Ch, int? Order, int OriginalIdx) b) {
      if (a.Order.HasValue && b.Order.HasValue) return a.Order.Value.CompareTo(b.Order.Value);
      if (a.Order.HasValue) return -1;
      if (b.Order.HasValue) return 1;
      return a.OriginalIdx.CompareTo(b.OriginalIdx);
    }
    preData.Sort(Compare);
    postData.Sort(Compare);

    // Emit.
    using var output = new System.IO.MemoryStream(data.Length);
    output.Write(_PngSignatureBytes, 0, _PngSignatureBytes.Length);
    _CopyChunkBytes(data, ihdr.Value, output);
    foreach (var (ch, _, _) in preData) _CopyChunkBytes(data, ch, output);
    if (fuseNames.Contains("IDAT") && idats.Count > 1)
      _WriteFusedIdat(data, idats, output);
    else
      foreach (var ch in idats) _CopyChunkBytes(data, ch, output);
    foreach (var (ch, _, _) in postData) _CopyChunkBytes(data, ch, output);
    if (iend is { } e) _CopyChunkBytes(data, e, output);
    else _WriteEmptyIend(output);

    return new ChunkRewriteResult { Bytes = output.ToArray() };
  }

  private static AllowedZones _ZoneToFlag(ChunkZone zone) => zone switch {
    ChunkZone.Signature => AllowedZones.Signature,
    ChunkZone.Header => AllowedZones.Header,
    ChunkZone.PreData => AllowedZones.PreData,
    ChunkZone.Data => AllowedZones.Data,
    ChunkZone.PostData => AllowedZones.PostData,
    ChunkZone.Footer => AllowedZones.Footer,
    _ => AllowedZones.None,
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
