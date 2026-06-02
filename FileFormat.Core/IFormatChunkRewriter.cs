using System;
using System.Collections.Generic;

namespace FileFormat.Core;

/// <summary>Where a chunk should end up after a rewrite. Matches the by-name policy model used by
/// downstream tools (CompressionWorkbench MetadataPlacementProfile, web-asset post-processors) so
/// callers don't need to reason about exact offsets.</summary>
public enum ChunkPlacement {
  /// <summary>Keep the chunk where it is (default — no-op for rules whose chunk is already valid).</summary>
  Keep,
  /// <summary>Move the chunk to be before the format's primary <see cref="ChunkKind.PixelData"/> run.</summary>
  BeforeData,
  /// <summary>Move the chunk to be after the format's primary <see cref="ChunkKind.PixelData"/> run
  /// (but before any mandatory footer such as PNG IEND).</summary>
  AfterData,
  /// <summary>Delete every instance of this chunk. The rewriter validates the chunk's
  /// <see cref="ChunkMobility.Removable"/> flag before honouring this.</summary>
  Remove,
  /// <summary>Merge every instance of this chunk into a single contiguous chunk in the chunk's
  /// existing position (PNG IDAT joining, JPEG split-marker fusing). Validates against
  /// <see cref="ChunkMobility.Fusible"/>.</summary>
  Fuse,
}

/// <summary>A single rewrite directive targeting all chunks of a given name.</summary>
/// <param name="Name">Format-specific chunk identifier (e.g. <c>"eXIf"</c>, <c>"IDAT"</c>, <c>"APP1"</c>).
/// Comparison is ordinal (PNG 4cc are case-sensitive: <c>tEXt</c> ≠ <c>TEXT</c>).</param>
/// <param name="Placement">What to do with chunks of this name.</param>
public readonly record struct ChunkRewriteRule(string Name, ChunkPlacement Placement);

/// <summary>Implemented by formats that can rewrite their on-disk byte sequence in response to caller-supplied
/// placement rules. The implementation owns format-integrity — CRCs, mandatory chunk ordering, internal
/// offset pointers, and any size-related book-keeping are recomputed automatically.</summary>
/// <remarks>
/// The rules are interpreted by-name (every instance of a given chunk name moves together). For per-instance
/// control the caller can pre-rename ordinals via the format's own API and then issue rules — most consumers
/// only need by-name semantics.
/// <para/>
/// Rules referencing names that don't appear in <paramref name="data"/> are silently ignored. Rules whose
/// chunk's <see cref="ChunkMobility"/> doesn't permit the requested placement are also ignored (no exception)
/// to keep the API resilient for "best-effort" callers — use
/// <see cref="IFormatChunkLayout{TSelf}.EnumerateChunks(ReadOnlySpan{byte})"/> first if strict validation is wanted.
/// </remarks>
public interface IFormatChunkRewriter<TSelf> where TSelf : IFormatChunkRewriter<TSelf> {

  /// <summary>Applies <paramref name="rules"/> to <paramref name="data"/> and returns the rewritten file bytes.
  /// Returns a copy of the input when no rule changes anything.</summary>
  static abstract byte[] Rewrite(ReadOnlySpan<byte> data, IReadOnlyList<ChunkRewriteRule> rules);
}
