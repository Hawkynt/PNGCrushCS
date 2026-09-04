namespace FileFormat.JpegXl.Codec;

/// <summary>Which modular stream a decode call is reading, and how much of it.</summary>
/// <remarks>
/// libjxl <c>ModularOptions</c> plus the stream's identity. None of this is in
/// the bitstream: the caller knows whether it is reading a frame's global stream
/// or one of its groups, and the stream reads differently in each case.
/// </remarks>
internal readonly record struct JxlModularStreamOptions {

  /// <summary>One stream carrying everything, which is what a single-group frame is.</summary>
  public static JxlModularStreamOptions WholeImage => new() {
    MaxChannelSize = int.MaxValue,
    StreamId = 0,
    UndoTransforms = true,
  };

  /// <summary>
  /// The largest a channel may be and still belong to this stream. The global
  /// stream stops at the first channel bigger than a group; a group's own stream
  /// has no limit, because everything in it is already a group's worth.
  /// </summary>
  public int MaxChannelSize { get; init; }

  /// <summary>The stream's number, which an MA tree may split on as property one.</summary>
  public int StreamId { get; init; }

  /// <summary>
  /// Whether the transforms come off at the end of this stream. They wait when
  /// the groups have yet to fill in the channels they apply to.
  /// </summary>
  public bool UndoTransforms { get; init; }
}

/// <summary>What one modular stream produced: its channels, and the transforms still to come off.</summary>
internal readonly record struct JxlModularStream(JxlModularImage Image, JxlModularTransform[] Transforms);
