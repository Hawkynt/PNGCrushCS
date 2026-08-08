using System;
using System.Collections.Generic;

namespace FileFormat.Core;

/// <summary>
/// Builds a <see cref="ChunkRewritePlan"/> that removes byte-identical duplicate chunks, on top of the
/// generic <see cref="IFormatChunkLayout{TSelf}"/>/<see cref="IFormatChunkPlanRewriter{TSelf}"/>
/// substrate — this is format-agnostic: it never looks at a chunk's payload semantics, only its raw
/// bytes, so it works for any format that exposes chunk layout, not just the metadata carriers.
/// </summary>
/// <remarks>
/// Only <see cref="ChunkMobility.Removable"/> chunks are ever removed, and the first occurrence of
/// each distinct (name, content) pair is always kept — this drops accidental duplication (the same
/// EXIF block written twice, a comment repeated by a careless tool) without touching two chunks that
/// merely share a name but carry different content. The result flows straight into
/// <see cref="IFormatChunkPlanRewriter{TSelf}.ApplyPlan"/>, so it's atomic and format-integrity-checked
/// like every other plan-based rewrite: it never partially deletes.
/// </remarks>
public static class ChunkOptimizer {

  /// <summary>Finds duplicate-content chunks of a given <paramref name="kind"/> (defaulting to
  /// <see cref="ChunkKind.Metadata"/>) and returns a plan that removes every occurrence after the
  /// first. Returns an empty plan (nothing to remove) when there are no duplicates.</summary>
  public static ChunkRewritePlan SuggestDeduplicationPlan(
    IReadOnlyList<ChunkSpan> chunks, ReadOnlySpan<byte> data, ChunkKind kind = ChunkKind.Metadata) {
    ArgumentNullException.ThrowIfNull(chunks);

    var seen = new Dictionary<(string Name, string Hash), bool>();
    var toRemove = new List<ChunkReference>();

    foreach (var chunk in chunks) {
      if (chunk.Kind != kind || (chunk.Mobility & ChunkMobility.Removable) == 0)
        continue;

      if (chunk.Offset < 0 || chunk.Length < 0 || chunk.Offset + chunk.Length > data.Length)
        continue; // malformed span — leave it alone rather than guess.

      var content = data.Slice((int)chunk.Offset, (int)chunk.Length);
      var key = (chunk.Name, _Hash(content));

      if (seen.ContainsKey(key))
        toRemove.Add(new ChunkReference(chunk.Name, chunk.Ordinal));
      else
        seen[key] = true;
    }

    return new ChunkRewritePlan { Remove = toRemove };
  }

  /// <summary>A cheap, stable content fingerprint — collisions would only cause a false "these are
  /// duplicates" merge, so this doesn't need cryptographic strength, just low accidental-collision
  /// odds for chunk-sized payloads.</summary>
  private static string _Hash(ReadOnlySpan<byte> data) {
    var hash = new System.Text.StringBuilder(32);
    Span<byte> digest = stackalloc byte[32];
    System.Security.Cryptography.SHA256.HashData(data, digest);
    foreach (var b in digest)
      hash.Append(b.ToString("x2"));
    return hash.ToString();
  }
}
