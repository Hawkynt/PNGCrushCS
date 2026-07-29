using System;

namespace FileFormat.Jbig2;

/// <summary>Represents a single segment in a JBIG2 file.</summary>
public sealed class Jbig2Segment {

  /// <summary>Segment number (4 bytes).</summary>
  public int Number { get; init; }

  /// <summary>Segment type from the header flags.</summary>
  public Jbig2SegmentType Type { get; init; }

  /// <summary>Whether the deferred non-retain flag is set.</summary>
  public bool DeferredNonRetain { get; init; }

  /// <summary>Segment numbers this segment refers to.</summary>
  public int[] ReferredSegments { get; init; } = [];

  /// <summary>Page association number (0 = file-level, >0 = specific page).</summary>
  public int PageAssociation { get; init; }

  /// <summary>Raw segment data bytes.</summary>
  public byte[] Data { get; init; } = [];
}
