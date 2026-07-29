using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using FileFormat.Core;

namespace FileFormat.Gif;

/// <summary>
/// GIF byte-level layout + by-name + plan-based rewrite.
/// </summary>
/// <remarks>
/// Chunk names:
/// <list type="bullet">
///   <item><b>SIGNATURE</b> — magic (6 bytes) + Logical Screen Descriptor (7 bytes) = 13 bytes at offset 0.</item>
///   <item><b>GCT</b> — Global Colour Table.</item>
///   <item><b>APP_NETSCAPE</b>, <b>APP_XMP</b>, <b>APP_ICC</b>, <b>APP_</b>&lt;identifier&gt; — application extensions.</item>
///   <item><b>COMMENT</b> — Comment Extension blocks.</item>
///   <item><b>PLAINTEXT</b> — Plain Text Extension blocks.</item>
///   <item><b>GCE</b> — Graphic Control Extension preceding a frame.</item>
///   <item><b>FRAME</b> — one span covering the Image Descriptor + optional Local Colour Table + LZW data
///   sub-blocks. Frame contents stay together — moving them apart would break the animation semantics.</item>
///   <item><b>TRAILER</b> — the single-byte 0x3B end-of-stream marker.</item>
/// </list>
/// Movable + removable: <b>COMMENT</b>, all application extensions, <b>PLAINTEXT</b>. The frame quadruples,
/// the GCT, the signature/LSD pair, and the trailer are <see cref="ChunkMobility.Fixed"/>.
/// </remarks>
internal static class GifChunkLayout {

  private const string _Signature = "SIGNATURE";
  private const string _Gct = "GCT";
  private const string _Trailer = "TRAILER";

  public static IReadOnlyList<ChunkSpan> Enumerate(ReadOnlySpan<byte> data) {
    var result = new List<ChunkSpan>();
    if (data.Length < 13) return result;
    if (data[0] != 'G' || data[1] != 'I' || data[2] != 'F') return result;

    var ordinals = new Dictionary<string, int>(StringComparer.Ordinal);
    int NextOrdinal(string name) {
      var n = ordinals.TryGetValue(name, out var v) ? v : 0;
      ordinals[name] = n + 1;
      return n;
    }

    // Signature = magic + LSD packed together (13 bytes).
    result.Add(new ChunkSpan(_Signature, 0, 13, ChunkKind.Signature, ChunkMobility.Fixed,
      NextOrdinal(_Signature), ChunkZone.Signature, AllowedZones.Signature));

    var pos = 13;
    var packed = data[10];
    var hasGct = (packed & 0x80) != 0;
    var gctSize = packed & 0x07;
    if (hasGct) {
      var gctBytes = (1 << (gctSize + 1)) * 3;
      if (pos + gctBytes > data.Length) return result;
      result.Add(new ChunkSpan(_Gct, pos, gctBytes, ChunkKind.Palette, ChunkMobility.Fixed,
        NextOrdinal(_Gct), ChunkZone.Header, AllowedZones.Header));
      pos += gctBytes;
    }

    var firstFrameIdx = -1;
    var pendingGceStart = -1;

    while (pos < data.Length) {
      var introducer = data[pos];
      if (introducer == 0x3B) {
        result.Add(new ChunkSpan(_Trailer, pos, 1, ChunkKind.Footer, ChunkMobility.Fixed,
          NextOrdinal(_Trailer), ChunkZone.Footer, AllowedZones.Footer));
        pos += 1;
        break;
      }

      if (introducer == 0x21) { // Extension
        if (pos + 2 > data.Length) break;
        var label = data[pos + 1];
        var blockStart = pos;

        if (label == 0xF9) {
          // Graphic Control Extension — bind to the next Image Descriptor as part of the FRAME span.
          var blockLen = _ScanFixedExtension(data, pos, 4);
          if (blockLen < 0) break;
          pendingGceStart = blockStart;
          pos += blockLen;
          continue;
        }

        if (label == 0xFE) {
          var blockLen = _ScanSubBlockChain(data, pos + 2);
          if (blockLen < 0) break;
          var total = 2 + blockLen;
          result.Add(new ChunkSpan("COMMENT", blockStart, total, ChunkKind.Metadata,
            ChunkMobility.Movable | ChunkMobility.Removable,
            NextOrdinal("COMMENT"), ChunkZone.PreData, AllowedZones.PreData | AllowedZones.PostData));
          pos += total;
          continue;
        }

        if (label == 0xFF) {
          // Application extension: 11-byte fixed block, then sub-block chain.
          if (pos + 3 > data.Length) break;
          var fixedSize = data[pos + 2];
          if (fixedSize != 11 || pos + 3 + 11 > data.Length) {
            // Malformed — preserve as opaque chunk.
            var fallback = _ScanFixedExtension(data, pos, fixedSize);
            if (fallback < 0) break;
            var subLen = _ScanSubBlockChain(data, pos + fallback);
            if (subLen < 0) break;
            var total = fallback + subLen;
            result.Add(new ChunkSpan("APP_UNKNOWN", blockStart, total, ChunkKind.Unknown, ChunkMobility.Fixed,
              NextOrdinal("APP_UNKNOWN"), ChunkZone.PreData, AllowedZones.PreData));
            pos += total;
            continue;
          }
          var identifier = Encoding.ASCII.GetString(data.Slice(pos + 3, 8));
          var name = _ApplicationExtensionName(identifier);
          var subBlockLen = _ScanSubBlockChain(data, pos + 14);
          if (subBlockLen < 0) break;
          var totalLen = 14 + subBlockLen;
          var (kind, mobility, allowed) = _ApplicationExtensionMetadata(name);
          result.Add(new ChunkSpan(name, blockStart, totalLen, kind, mobility,
            NextOrdinal(name), ChunkZone.PreData, allowed));
          pos += totalLen;
          continue;
        }

        if (label == 0x01) {
          var blockLen = _ScanFixedExtension(data, pos, 12);
          if (blockLen < 0) break;
          var subLen = _ScanSubBlockChain(data, pos + blockLen);
          if (subLen < 0) break;
          var total = blockLen + subLen;
          result.Add(new ChunkSpan("PLAINTEXT", blockStart, total, ChunkKind.Metadata,
            ChunkMobility.Movable | ChunkMobility.Removable,
            NextOrdinal("PLAINTEXT"), ChunkZone.PreData, AllowedZones.PreData | AllowedZones.PostData));
          pos += total;
          continue;
        }

        // Unknown extension label — opaque, treat as fixed.
        var unknownBlockLen = _ScanFixedExtension(data, pos, data[pos + 2]);
        if (unknownBlockLen < 0) break;
        var unknownSubLen = _ScanSubBlockChain(data, pos + unknownBlockLen);
        if (unknownSubLen < 0) break;
        var unknownTotal = unknownBlockLen + unknownSubLen;
        result.Add(new ChunkSpan("EXT_UNKNOWN", blockStart, unknownTotal, ChunkKind.Unknown, ChunkMobility.Fixed,
          NextOrdinal("EXT_UNKNOWN"), ChunkZone.PreData, AllowedZones.PreData));
        pos += unknownTotal;
        continue;
      }

      if (introducer == 0x2C) {
        // Frame = (optional GCE) + Image Descriptor + (LCT) + LZW data sub-blocks.
        var frameStart = pendingGceStart >= 0 ? pendingGceStart : pos;
        pendingGceStart = -1;

        if (pos + 10 > data.Length) break;
        var idPacked = data[pos + 9];
        var hasLct = (idPacked & 0x80) != 0;
        var lctSizeExp = idPacked & 0x07;
        var lctBytes = hasLct ? (1 << (lctSizeExp + 1)) * 3 : 0;
        var afterId = pos + 10 + lctBytes;
        if (afterId + 1 > data.Length) break;
        // Skip 1-byte LZW min code size + sub-blocks.
        var subBlockLen = _ScanSubBlockChain(data, afterId + 1);
        if (subBlockLen < 0) break;
        var frameEnd = afterId + 1 + subBlockLen;
        var frameLen = frameEnd - frameStart;
        if (firstFrameIdx < 0) firstFrameIdx = result.Count;
        result.Add(new ChunkSpan("FRAME", frameStart, frameLen, ChunkKind.PixelData, ChunkMobility.Fixed,
          NextOrdinal("FRAME"), ChunkZone.Data, AllowedZones.Data));
        pos = frameEnd;
        continue;
      }

      // Unknown byte — skip tolerantly.
      pos++;
    }

    // Final pass: chunks recorded as PreData but that appear AFTER all frames are reclassified as PostData
    // so the rewriter can move metadata across the data zone cleanly.
    if (firstFrameIdx >= 0) {
      // Find the last frame's index.
      var lastFrameIdx = -1;
      for (var i = result.Count - 1; i >= 0; --i) if (result[i].Name == "FRAME") { lastFrameIdx = i; break; }
      if (lastFrameIdx >= 0) {
        for (var i = lastFrameIdx + 1; i < result.Count; ++i) {
          var c = result[i];
          if (c.Name == _Trailer) continue;
          if (c.CurrentZone != ChunkZone.PreData) continue;
          var allowed = (c.Mobility & ChunkMobility.Movable) != 0
            ? AllowedZones.PreData | AllowedZones.PostData
            : c.AllowedZones;
          result[i] = c with { CurrentZone = ChunkZone.PostData, AllowedZones = allowed };
        }
      }
    }

    return result;
  }

  // -- helpers -----------------------------------------------------------------

  /// <summary>Length of a fixed-size extension (introducer + label + size byte + size bytes). Returns -1 on truncation.</summary>
  private static int _ScanFixedExtension(ReadOnlySpan<byte> data, int pos, int expectedSize) {
    if (pos + 3 + expectedSize > data.Length) return -1;
    return 3 + expectedSize;
  }

  /// <summary>Length of a sub-block chain starting at <paramref name="offset"/>, including the trailing
  /// zero terminator. Returns -1 on truncation.</summary>
  private static int _ScanSubBlockChain(ReadOnlySpan<byte> data, int offset) {
    var pos = offset;
    while (pos < data.Length) {
      var sz = data[pos];
      pos += 1;
      if (sz == 0) return pos - offset;
      if (pos + sz > data.Length) return -1;
      pos += sz;
    }
    return -1;
  }

  private static string _ApplicationExtensionName(string identifier) => identifier switch {
    "NETSCAPE" => "APP_NETSCAPE",
    "ANIMEXTS" => "APP_NETSCAPE",
    "XMP Data" => "APP_XMP",
    "ICCRGBG1" => "APP_ICC",
    _ => "APP_" + identifier.TrimEnd(),
  };

  private static (ChunkKind, ChunkMobility, AllowedZones) _ApplicationExtensionMetadata(string name) => name switch {
    "APP_NETSCAPE" => (ChunkKind.Metadata, ChunkMobility.Movable | ChunkMobility.Removable,
                       AllowedZones.PreData | AllowedZones.PostData),
    "APP_XMP" => (ChunkKind.Metadata, ChunkMobility.Movable | ChunkMobility.Removable,
                  AllowedZones.PreData | AllowedZones.PostData),
    "APP_ICC" => (ChunkKind.ColorProfile, ChunkMobility.Movable | ChunkMobility.Removable,
                  AllowedZones.PreData | AllowedZones.PostData),
    _ => (ChunkKind.Metadata, ChunkMobility.Movable | ChunkMobility.Removable,
          AllowedZones.PreData | AllowedZones.PostData),
  };

  // ============================================================
  // by-name rewrite (lenient)
  // ============================================================

  public static byte[] Rewrite(ReadOnlySpan<byte> data, IReadOnlyList<ChunkRewriteRule> rules) {
    var chunks = Enumerate(data);
    if (chunks.Count == 0) return data.ToArray();

    var preData = new List<ChunkSpan>();
    var dataZone = new List<ChunkSpan>();
    var postData = new List<ChunkSpan>();
    ChunkSpan? signature = null;
    ChunkSpan? gct = null;
    ChunkSpan? trailer = null;

    foreach (var ch in chunks) {
      if (ch.Name == _Signature) { signature = ch; continue; }
      if (ch.Name == _Gct) { gct = ch; continue; }
      if (ch.Name == _Trailer) { trailer = ch; continue; }
      switch (ch.CurrentZone) {
        case ChunkZone.PreData: preData.Add(ch); break;
        case ChunkZone.Data: dataZone.Add(ch); break;
        case ChunkZone.PostData: postData.Add(ch); break;
      }
    }

    foreach (var rule in rules) {
      switch (rule.Placement) {
        case ChunkPlacement.Remove:
          _RemoveAllNamed(preData, rule.Name);
          _RemoveAllNamed(postData, rule.Name);
          break;
        case ChunkPlacement.BeforeData:
          _MoveAllNamed(postData, preData, rule.Name);
          break;
        case ChunkPlacement.AfterData:
          _MoveAllNamed(preData, postData, rule.Name);
          break;
      }
    }

    return _Emit(data, signature, gct, preData, dataZone, postData, trailer);
  }

  // ============================================================
  // plan-based rewrite (strict)
  // ============================================================

  public static ChunkRewriteResult ApplyPlan(ReadOnlySpan<byte> data, ChunkRewritePlan plan) {
    var failures = new List<ChunkRewriteFailure>();
    var chunks = Enumerate(data);
    if (chunks.Count == 0) {
      failures.Add(new ChunkRewriteFailure("Validate", "(file)", 0, "Not a valid GIF."));
      return new ChunkRewriteResult { Failures = failures };
    }

    var byRef = new Dictionary<ChunkReference, ChunkSpan>();
    foreach (var ch in chunks) byRef[new ChunkReference(ch.Name, ch.Ordinal)] = ch;

    var requestedZone = new Dictionary<ChunkReference, (ChunkZone Zone, int? Order)>();
    foreach (var p in plan.Placements) {
      if (!byRef.TryGetValue(p.Chunk, out var ch)) {
        failures.Add(new ChunkRewriteFailure("Place", p.Chunk.Name, p.Chunk.Ordinal,
          $"No chunk with name '{p.Chunk.Name}' and ordinal {p.Chunk.Ordinal} found."));
        continue;
      }
      if ((ch.AllowedZones & _ZoneToFlag(p.TargetZone)) == 0) {
        failures.Add(new ChunkRewriteFailure("Place", p.Chunk.Name, p.Chunk.Ordinal,
          $"Chunk '{p.Chunk.Name}' may not occupy zone {p.TargetZone}. Allowed: {ch.AllowedZones}."));
        continue;
      }
      requestedZone[p.Chunk] = (p.TargetZone, p.OrderInZone);
    }

    var toRemove = new HashSet<ChunkReference>();
    foreach (var r in plan.Remove) {
      if (!byRef.TryGetValue(r, out var ch)) {
        failures.Add(new ChunkRewriteFailure("Remove", r.Name, r.Ordinal, "No chunk found."));
        continue;
      }
      if ((ch.Mobility & ChunkMobility.Removable) == 0) {
        failures.Add(new ChunkRewriteFailure("Remove", r.Name, r.Ordinal,
          $"Chunk '{r.Name}' is not removable (mobility: {ch.Mobility})."));
        continue;
      }
      toRemove.Add(r);
    }

    foreach (var name in plan.Fuse)
      failures.Add(new ChunkRewriteFailure("Fuse", name, 0, "GIF does not support chunk fusion."));

    if (failures.Count > 0)
      return new ChunkRewriteResult { Failures = failures };

    var preData = new List<(ChunkSpan Ch, int? Order, int OriginalIdx)>();
    var postData = new List<(ChunkSpan Ch, int? Order, int OriginalIdx)>();
    var dataZone = new List<ChunkSpan>();
    ChunkSpan? signature = null;
    ChunkSpan? gct = null;
    ChunkSpan? trailer = null;

    for (var i = 0; i < chunks.Count; ++i) {
      var ch = chunks[i];
      if (ch.Name == _Signature) { signature = ch; continue; }
      if (ch.Name == _Gct) { gct = ch; continue; }
      if (ch.Name == _Trailer) { trailer = ch; continue; }
      if (toRemove.Contains(new ChunkReference(ch.Name, ch.Ordinal))) continue;
      var key = new ChunkReference(ch.Name, ch.Ordinal);
      var (zone, order) = requestedZone.TryGetValue(key, out var pz) ? pz : (ch.CurrentZone, (int?)null);
      switch (zone) {
        case ChunkZone.PreData: preData.Add((ch, order, i)); break;
        case ChunkZone.PostData: postData.Add((ch, order, i)); break;
        default: dataZone.Add(ch); break;
      }
    }

    static int Compare((ChunkSpan Ch, int? Order, int OriginalIdx) a, (ChunkSpan Ch, int? Order, int OriginalIdx) b) {
      if (a.Order.HasValue && b.Order.HasValue) return a.Order.Value.CompareTo(b.Order.Value);
      if (a.Order.HasValue) return -1;
      if (b.Order.HasValue) return 1;
      return a.OriginalIdx.CompareTo(b.OriginalIdx);
    }
    preData.Sort(Compare);
    postData.Sort(Compare);

    return new ChunkRewriteResult {
      Bytes = _Emit(data, signature, gct,
        preData.ConvertAll(t => t.Ch),
        dataZone,
        postData.ConvertAll(t => t.Ch),
        trailer),
    };
  }

  // ============================================================
  // emitter
  // ============================================================

  private static byte[] _Emit(
    ReadOnlySpan<byte> data,
    ChunkSpan? signature,
    ChunkSpan? gct,
    List<ChunkSpan> preData,
    List<ChunkSpan> dataZone,
    List<ChunkSpan> postData,
    ChunkSpan? trailer
  ) {
    using var output = new MemoryStream(data.Length);
    if (signature is { } s) output.Write(data.Slice((int)s.Offset, (int)s.Length));
    if (gct is { } g) output.Write(data.Slice((int)g.Offset, (int)g.Length));
    foreach (var ch in preData) output.Write(data.Slice((int)ch.Offset, (int)ch.Length));
    foreach (var ch in dataZone) output.Write(data.Slice((int)ch.Offset, (int)ch.Length));
    foreach (var ch in postData) output.Write(data.Slice((int)ch.Offset, (int)ch.Length));
    output.Write(trailer is { } t ? data.Slice((int)t.Offset, (int)t.Length) : (ReadOnlySpan<byte>)new byte[] { 0x3B });
    return output.ToArray();
  }

  private static void _RemoveAllNamed(List<ChunkSpan> bucket, string name) {
    for (var i = bucket.Count - 1; i >= 0; --i) {
      var ch = bucket[i];
      if (ch.Name != name) continue;
      if ((ch.Mobility & ChunkMobility.Removable) == 0) continue;
      bucket.RemoveAt(i);
    }
  }

  private static void _MoveAllNamed(List<ChunkSpan> source, List<ChunkSpan> dest, string name) {
    for (var i = source.Count - 1; i >= 0; --i) {
      var ch = source[i];
      if (ch.Name != name) continue;
      if ((ch.Mobility & ChunkMobility.Movable) == 0) continue;
      source.RemoveAt(i);
      dest.Add(ch);
    }
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
}
