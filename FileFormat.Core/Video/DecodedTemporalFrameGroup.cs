using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace FileFormat.Core;

/// <summary>
/// An ordered group of decoded presentation frames that were coupled by one codec operation.
/// </summary>
/// <remarks>
/// This is deliberately different from <see cref="RawMultiViewImage"/>. Multi-view images are
/// simultaneous camera/eye views of one presentation instant. A temporal frame group contains
/// different presentation instants whose coded representation happened to share one transform or
/// packet. GoPro's legacy 2003 CineForm volumetric/3-D wavelet is the motivating example: it compresses
/// two temporal frames together but is explicitly not stereoscopic.
/// <para/>
/// Frames retain their own <see cref="DecodedFrame.PresentationTimestamp"/>, stream identity and
/// key-frame state. The group preserves presentation order; when adjacent timestamps are both known
/// they must therefore be strictly increasing.
/// </remarks>
public sealed class DecodedTemporalFrameGroup {
  private readonly ReadOnlyCollection<DecodedFrame> _frames;

  /// <summary>Groups two or more reconstructed frames that one codec operation produced together.</summary>
  /// <param name="frames">
  /// The frames in presentation order. They must come from one stream, share geometry and
  /// <see cref="PixelFormat"/>, and where two adjacent presentation timestamps are both known the later
  /// one must be greater.
  /// </param>
  /// <exception cref="ArgumentException">
  /// Fewer than two frames, a frame without a raster, a frame from another stream, a raster whose
  /// geometry or format differs from the first, or adjacent known timestamps that do not increase.
  /// </exception>
  public DecodedTemporalFrameGroup(IEnumerable<DecodedFrame> frames) {
    ArgumentNullException.ThrowIfNull(frames);

    var materialized = frames.ToArray();
    if (materialized.Length < 2)
      throw new ArgumentException("A temporal frame group needs at least two presentation frames.", nameof(frames));

    var first = materialized[0];
    if (first.Image is null)
      throw new ArgumentException("A temporal frame group cannot contain a null raster.", nameof(frames));

    this.StreamIndex = first.StreamIndex;
    this.Width = first.Image.Width;
    this.Height = first.Image.Height;
    this.Format = first.Image.Format;

    for (var i = 0; i < materialized.Length; ++i) {
      var frame = materialized[i];
      if (frame.Image is null)
        throw new ArgumentException($"Temporal frame {i} has no raster.", nameof(frames));

      if (frame.StreamIndex != this.StreamIndex)
        throw new ArgumentException(
          $"Temporal frame {i} belongs to stream {frame.StreamIndex}; the group belongs to stream {this.StreamIndex}.",
          nameof(frames));

      if (frame.Image.Width != this.Width || frame.Image.Height != this.Height || frame.Image.Format != this.Format)
        throw new ArgumentException(
          $"Temporal frame {i} is {frame.Image.Width}x{frame.Image.Height} {frame.Image.Format}; all frames in one transform group must match {this.Width}x{this.Height} {this.Format}.",
          nameof(frames));

      if (i == 0)
        continue;

      var previousTimestamp = materialized[i - 1].PresentationTimestamp;
      if (previousTimestamp.HasValue && frame.PresentationTimestamp.HasValue && frame.PresentationTimestamp <= previousTimestamp)
        throw new ArgumentException(
          $"Temporal frame {i} has presentation timestamp {frame.PresentationTimestamp}; presentation order requires it to follow {previousTimestamp}.",
          nameof(frames));
    }

    this._frames = Array.AsReadOnly(materialized);
  }

  /// <summary>Frames in presentation order.</summary>
  public IReadOnlyList<DecodedFrame> Frames => this._frames;

  /// <summary>How many frames the coding operation produced together; always at least two.</summary>
  public int Count => this._frames.Count;

  /// <summary>The one stream every frame in the group belongs to.</summary>
  public int StreamIndex { get; }

  /// <summary>Width shared by every frame in the group.</summary>
  public int Width { get; }

  /// <summary>Height shared by every frame in the group.</summary>
  public int Height { get; }

  /// <summary>Pixel representation shared by every frame in the group.</summary>
  public PixelFormat Format { get; }

  /// <summary>Gets the frame at one presentation-order position.</summary>
  public DecodedFrame this[int index] => this._frames[index];
}
