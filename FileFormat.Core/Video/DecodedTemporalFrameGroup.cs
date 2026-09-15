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

  public DecodedTemporalFrameGroup(IEnumerable<DecodedFrame> frames) {
    ArgumentNullException.ThrowIfNull(frames);

    var materialized = frames.ToArray();
    if (materialized.Length < 2)
      throw new ArgumentException("A temporal frame group needs at least two presentation frames.", nameof(frames));

    var first = materialized[0];
    this.StreamIndex = first.StreamIndex;
    this.Width = first.Image.Width;
    this.Height = first.Image.Height;
    this.Format = first.Image.Format;

    for (var i = 0; i < materialized.Length; ++i) {
      var frame = materialized[i];
      ArgumentNullException.ThrowIfNull(frame.Image);

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

  public int Count => this._frames.Count;
  public int StreamIndex { get; }
  public int Width { get; }
  public int Height { get; }
  public PixelFormat Format { get; }

  /// <summary>Gets the frame at one presentation-order position.</summary>
  public DecodedFrame this[int index] => this._frames[index];
}
