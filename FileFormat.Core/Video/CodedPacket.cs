using System;
using System.Collections.Generic;

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
/// <param name="FragmentOffsets">Where in <see cref="Data"/> each of the pieces the container carried
/// this in begins, or <c>null</c> where the container carried it in one piece and said so by not
/// cutting it.</param>
public readonly record struct CodedPacket(
  int StreamIndex,
  ReadOnlyMemory<byte> Data,
  long? PresentationTimestamp = null,
  long? DecodeTimestamp = null,
  long? Duration = null,
  bool IsKeyFrame = false,
  IReadOnlyList<int>? FragmentOffsets = null) {

  /// <summary>
  /// Where each piece this was carried in begins, with a single piece at nought where the container
  /// cut none.
  /// </summary>
  /// <remarks>
  /// A container that cuts a coded unit into pieces knows where it cut, and that knowledge dies with
  /// the reassembly unless it is carried. For most containers it is worth nothing — an ASF payload or
  /// a Matroska block is cut wherever the packet size ran out, at no boundary the codec would
  /// recognise — but RealMedia cuts a picture at its slices, one slice to a piece, and a RealVideo
  /// picture's slices are not otherwise findable: they carry no start code and the bit padding between
  /// them is not fixed.
  /// <para/>
  /// It is stated as offsets and not as a prefix on the bytes on purpose. ffmpeg's demuxer writes a
  /// small table of them in front of every RealVideo packet it hands out, which works and is why a
  /// packet from it is eight bytes a slice longer than the picture; but a byte layout invented by one
  /// demuxer for one decoder is exactly the kind of private arrangement the split between the two
  /// exists to prevent. A demuxer here says where it cut and says it in the model, a decoder that has
  /// a use for that reads it, and one that has not is unaffected — no codec has to know how a
  /// container spells anything.
  /// </remarks>
  public IReadOnlyList<int> Fragments => this.FragmentOffsets ?? _WHOLE;

  private static readonly int[] _WHOLE = [0];
}
