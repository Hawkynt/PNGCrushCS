using System;
using System.Collections.Generic;

namespace FileFormat.Core;

/// <summary>A single chunk instance, identified by its name + ordinal pair. Use this when a chunk name
/// can repeat (e.g. multiple PNG IDATs) and the caller wants to address one specific occurrence.</summary>
public readonly record struct ChunkReference(string Name, int Ordinal = 0);

/// <summary>An explicit placement directive — "put this chunk into that zone, optionally at this order
/// within the zone". Plan-based callers build a list of these to express a concrete desired layout.</summary>
/// <param name="Chunk">The chunk to place (Name + Ordinal).</param>
/// <param name="TargetZone">The zone the chunk should land in. Must be present in the chunk's
/// <see cref="ChunkSpan.AllowedZones"/> bitmask or the rewriter will reject the directive.</param>
/// <param name="OrderInZone">When multiple placements target the same zone, this controls intra-zone
/// ordering. Lower values come first; equal values fall back to enumeration order. <c>null</c> means
/// "preserve the chunk's existing relative order within the zone".</param>
public readonly record struct ChunkPlacementDirective(
  ChunkReference Chunk,
  ChunkZone TargetZone,
  int? OrderInZone = null);

/// <summary>A concrete rewrite plan. Unlike <see cref="ChunkRewriteRule"/>'s by-name policy model, this
/// addresses each chunk instance individually via <see cref="ChunkReference"/>.</summary>
public sealed record ChunkRewritePlan {
  /// <summary>Explicit zone assignments. Chunks not mentioned here keep their current zone + order.</summary>
  public IReadOnlyList<ChunkPlacementDirective> Placements { get; init; } = Array.Empty<ChunkPlacementDirective>();

  /// <summary>Specific chunk instances to remove. Validated against <see cref="ChunkMobility.Removable"/>.</summary>
  public IReadOnlyList<ChunkReference> Remove { get; init; } = Array.Empty<ChunkReference>();

  /// <summary>Chunk names whose every instance should fuse into one. Validated against
  /// <see cref="ChunkMobility.Fusible"/>.</summary>
  public IReadOnlyList<string> Fuse { get; init; } = Array.Empty<string>();
}

/// <summary>One reason the rewriter refused a directive.</summary>
/// <param name="Operation">Which operation was rejected: "Place", "Remove", or "Fuse".</param>
/// <param name="ChunkName">Chunk identifier the directive targeted.</param>
/// <param name="Ordinal">Ordinal of the targeted instance (0 for Fuse, which is by-name).</param>
/// <param name="Reason">Human-readable explanation (e.g. <c>"PLTE cannot move to PostData — PNG spec
/// requires it to precede any IDAT."</c>).</param>
public sealed record ChunkRewriteFailure(
  string Operation,
  string ChunkName,
  int Ordinal,
  string Reason);

/// <summary>Outcome of <see cref="IFormatChunkPlanRewriter{TSelf}.ApplyPlan(ReadOnlySpan{byte}, ChunkRewritePlan)"/>.
/// On success <see cref="Bytes"/> is the rewritten file; <see cref="Failures"/> is empty.
/// On failure <see cref="Bytes"/> is <c>null</c> and every rejected directive appears in <see cref="Failures"/>
/// — the file is NOT partially rewritten.</summary>
public sealed record ChunkRewriteResult {
  public byte[]? Bytes { get; init; }
  public IReadOnlyList<ChunkRewriteFailure> Failures { get; init; } = Array.Empty<ChunkRewriteFailure>();
  public bool Success => this.Bytes != null;
}

/// <summary>Implemented by formats that accept concrete per-chunk placement plans and validate them
/// against format-integrity rules before rewriting. Refuses invalid plans instead of silently dropping
/// directives — the by-name <see cref="IFormatChunkRewriter{TSelf}"/> is the lenient counterpart.</summary>
/// <remarks>
/// The split lets callers choose their level of strictness:
/// <list type="bullet">
/// <item>by-name rules → best-effort, ignore invalid rules (good for "move all my eXIf forward, do your best")</item>
/// <item>plan → all-or-nothing, surface every problem (good for "I've computed exact layout; tell me if it breaks")</item>
/// </list>
/// </remarks>
public interface IFormatChunkPlanRewriter<TSelf> where TSelf : IFormatChunkPlanRewriter<TSelf> {

  /// <summary>Validates <paramref name="plan"/> against the format's ordering and mobility rules and,
  /// if every directive is legal, returns the rewritten file bytes. If any directive would invalidate
  /// the file, returns failure diagnostics and leaves <see cref="ChunkRewriteResult.Bytes"/> null —
  /// no partial rewrite is performed.</summary>
  static abstract ChunkRewriteResult ApplyPlan(ReadOnlySpan<byte> data, ChunkRewritePlan plan);
}
