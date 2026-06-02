using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using FileFormat.Core;

namespace FileFormat.WebP;

/// <summary>WebP byte-level layout + by-name + plan-based chunk rewrite.</summary>
/// <remarks>
/// WebP is RIFF: a 12-byte preamble (<c>RIFF</c> + 4-byte size + <c>WEBP</c>) followed by 4-byte-fourcc
/// chunks (4 cc + 4 size + data + optional 1 pad byte to even length). Image data chunks (VP8 / VP8L /
/// VP8X / ANIM / ANMF / ALPH) are locked in the Data zone; the metadata chunks <c>ICCP</c>,
/// <c>EXIF</c>, <c>XMP </c> are movable and removable; everything else falls back to Fixed.
/// <para/>
/// A RIFF rewrite must keep the outer length field consistent with the chunk total. We recompute it on
/// emit; the SIGNATURE span is split into a fixed prefix (8 bytes) + form type (4 bytes) so callers can
/// see the structure but only the length field gets patched.
/// </remarks>
internal static class WebPChunkLayout {

  private const string _Signature = "SIGNATURE";

  private static readonly byte[] _Riff = "RIFF"u8.ToArray();
  private static readonly byte[] _Webp = "WEBP"u8.ToArray();

  public static IReadOnlyList<ChunkSpan> Enumerate(ReadOnlySpan<byte> data) {
    var result = new List<ChunkSpan>();
    if (data.Length < 12) return result;
    for (var i = 0; i < 4; ++i) if (data[i] != _Riff[i]) return result;
    for (var i = 0; i < 4; ++i) if (data[8 + i] != _Webp[i]) return result;

    var ordinals = new Dictionary<string, int>(StringComparer.Ordinal);
    int NextOrdinal(string name) {
      var n = ordinals.TryGetValue(name, out var v) ? v : 0;
      ordinals[name] = n + 1;
      return n;
    }

    result.Add(new ChunkSpan(_Signature, 0, 12, ChunkKind.Signature, ChunkMobility.Fixed,
      Ordinal: NextOrdinal(_Signature), CurrentZone: ChunkZone.Signature, AllowedZones: AllowedZones.Signature));

    var pos = 12;
    // First pass to find where data chunks begin, so PreData metadata vs PostData metadata is distinguishable.
    var raw = new List<(string Name, int Offset, int Length, ChunkKind Kind, ChunkMobility Mobility, bool IsDataZone)>();
    var firstDataIdx = -1;
    while (pos + 8 <= data.Length) {
      var fourcc = System.Text.Encoding.ASCII.GetString(data.Slice(pos, 4)).TrimEnd();
      var ccRaw = System.Text.Encoding.ASCII.GetString(data.Slice(pos, 4)); // keep trailing space for "XMP "
      var size = (long)BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(pos + 4, 4));
      var pad = (size & 1) == 1 ? 1 : 0;
      var total = 8 + size + pad;
      if (pos + total > data.Length) {
        // Truncated; bail rather than over-read.
        total = data.Length - pos;
      }
      var (kind, mobility, isData) = _Classify(ccRaw);
      raw.Add((ccRaw, pos, (int)total, kind, mobility, isData));
      if (isData && firstDataIdx < 0) firstDataIdx = raw.Count - 1;
      pos += (int)total;
    }

    for (var i = 0; i < raw.Count; ++i) {
      var r = raw[i];
      var zone = r.IsDataZone
        ? ChunkZone.Data
        : firstDataIdx >= 0 && i > firstDataIdx ? ChunkZone.PostData : ChunkZone.PreData;
      var allowed = (r.Mobility & ChunkMobility.Movable) != 0
        ? AllowedZones.PreData | AllowedZones.PostData
        : r.IsDataZone ? AllowedZones.Data : AllowedZones.PreData;
      result.Add(new ChunkSpan(r.Name, r.Offset, r.Length, r.Kind, r.Mobility, NextOrdinal(r.Name), zone, allowed));
    }

    return result;
  }

  private static (ChunkKind Kind, ChunkMobility Mobility, bool IsDataZone) _Classify(string fourcc) => fourcc switch {
    "VP8 " or "VP8L" or "VP8X" or "ANIM" or "ANMF" or "ALPH" => (ChunkKind.PixelData, ChunkMobility.Fixed, true),
    "ICCP" => (ChunkKind.ColorProfile, ChunkMobility.Movable | ChunkMobility.Removable, false),
    "EXIF" => (ChunkKind.Metadata, ChunkMobility.Movable | ChunkMobility.Removable, false),
    "XMP " => (ChunkKind.Metadata, ChunkMobility.Movable | ChunkMobility.Removable, false),
    _ => (ChunkKind.Unknown, ChunkMobility.Fixed, false),
  };

  public static byte[] Rewrite(ReadOnlySpan<byte> data, IReadOnlyList<ChunkRewriteRule> rules) {
    var chunks = Enumerate(data);
    if (chunks.Count == 0) return data.ToArray();

    var preData = new List<ChunkSpan>();
    var dataZone = new List<ChunkSpan>();
    var postData = new List<ChunkSpan>();
    foreach (var ch in chunks) {
      if (ch.Name == _Signature) continue;
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

    return _Emit(data, preData, dataZone, postData);
  }

  public static ChunkRewriteResult ApplyPlan(ReadOnlySpan<byte> data, ChunkRewritePlan plan) {
    var failures = new List<ChunkRewriteFailure>();
    var chunks = Enumerate(data);
    if (chunks.Count == 0) {
      failures.Add(new ChunkRewriteFailure("Validate", "(file)", 0, "Not a valid WebP."));
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
      failures.Add(new ChunkRewriteFailure("Fuse", name, 0, "WebP does not support chunk fusion."));

    if (failures.Count > 0)
      return new ChunkRewriteResult { Failures = failures };

    var preData = new List<(ChunkSpan Ch, int? Order, int OriginalIdx)>();
    var postData = new List<(ChunkSpan Ch, int? Order, int OriginalIdx)>();
    var dataZone = new List<ChunkSpan>();

    for (var i = 0; i < chunks.Count; ++i) {
      var ch = chunks[i];
      if (ch.Name == _Signature) continue;
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
      Bytes = _Emit(data, preData.ConvertAll(t => t.Ch), dataZone, postData.ConvertAll(t => t.Ch)),
    };
  }

  private static byte[] _Emit(ReadOnlySpan<byte> data, List<ChunkSpan> preData, List<ChunkSpan> dataZone, List<ChunkSpan> postData) {
    // Compute new RIFF payload length: 4 ("WEBP") + sum of every chunk's length.
    var chunkBytes = 0L;
    foreach (var ch in preData) chunkBytes += ch.Length;
    foreach (var ch in dataZone) chunkBytes += ch.Length;
    foreach (var ch in postData) chunkBytes += ch.Length;
    var riffPayload = 4 + chunkBytes; // "WEBP" + chunks

    using var output = new MemoryStream((int)(8 + riffPayload));
    output.Write(_Riff);
    Span<byte> sizeBuf = stackalloc byte[4];
    BinaryPrimitives.WriteUInt32LittleEndian(sizeBuf, (uint)riffPayload);
    output.Write(sizeBuf);
    output.Write(_Webp);
    foreach (var ch in preData) output.Write(data.Slice((int)ch.Offset, (int)ch.Length));
    foreach (var ch in dataZone) output.Write(data.Slice((int)ch.Offset, (int)ch.Length));
    foreach (var ch in postData) output.Write(data.Slice((int)ch.Offset, (int)ch.Length));
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
