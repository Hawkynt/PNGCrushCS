using System;

namespace FileFormat.Core;

/// <summary>
/// One unit of coded data as it lies in a container: the bytes, the stream they belong to, and when
/// they are due.
/// </summary>
/// <remarks>
/// A packet is not a picture. For Motion JPEG one packet happens to hold exactly one whole frame,
/// but that is a property of that codec and not of the model — a packet of a predicted codec may
/// need the packets before it to mean anything, and a codec with bidirectional prediction produces
/// frames in an order that is not the packets' order. So a packet is passed to a decoder and a frame
/// is what may come back, rather than the two being the same thing under two names.
/// <para/>
/// <see cref="Data"/> is a window onto the container's own buffer wherever the container can manage
/// it, not a copy. A demuxer walking a film must not leave a copy of it behind.
/// </remarks>
/// <param name="StreamIndex">Which stream of the container this belongs to.</param>
/// <param name="Data">The coded bytes, in the codec's own layout.</param>
/// <param name="PresentationTimestamp">When this is due for display, counted in the stream's time
/// base, or <c>null</c> where the container states nothing and the position implies it.</param>
/// <param name="DecodeTimestamp">When this is due for decoding, which differs from the presentation
/// timestamp exactly when a codec reorders frames.</param>
/// <param name="Duration">How long this occupies, in the stream's time base, where stated.</param>
/// <param name="IsKeyFrame">Whether decoding may begin here without anything before it.</param>
public readonly record struct CodedPacket(
  int StreamIndex,
  ReadOnlyMemory<byte> Data,
  long? PresentationTimestamp = null,
  long? DecodeTimestamp = null,
  long? Duration = null,
  bool IsKeyFrame = false);
