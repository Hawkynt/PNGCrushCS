using FileFormat.Core;

namespace FileFormat.Matroska;

/// <summary>One <c>TrackEntry</c>: what a caller is told about it, and what timing it implies.</summary>
/// <remarks>
/// Internal because the two timing fields are this reader's own bookkeeping rather than anything a
/// caller has a use for — what a caller wants of a track is its <see cref="MediaStreamInfo"/>. They
/// live beside it rather than inside it because both are stated in nanoseconds, and every timestamp
/// this reader hands out is counted in the segment's own ticks; converting once per file is a
/// division, converting inside the walk would be a division per frame.
/// </remarks>
internal sealed class MatroskaTrack {

  /// <summary>What the container declares about this track.</summary>
  internal required MediaStreamInfo Info { get; init; }

  /// <summary>
  /// The number the blocks of this track carry, which is not its position among the tracks.
  /// </summary>
  /// <remarks>
  /// Track numbers start at one and a file may leave gaps in them — a track removed from a file
  /// keeps the others' numbers where they were. The stream index is the position, so the two have to
  /// be mapped rather than assumed equal.
  /// </remarks>
  internal required ulong Number { get; init; }

  /// <summary>How long one frame of this track lasts, in nanoseconds, or zero when unstated.</summary>
  /// <remarks>
  /// <c>DefaultDuration</c> is stated in nanoseconds however the segment scales its timestamps, so it
  /// is kept as written and converted where it is used.
  /// </remarks>
  internal long DefaultDurationNanoseconds { get; init; }

  /// <summary>What <c>CodecDelay</c> shifts this track's timestamps by, in the segment's ticks.</summary>
  /// <remarks>
  /// Measured: ffmpeg writes a <c>CodecDelay</c> of 2 902 494 ns into the Vorbis track of a file it
  /// muxes, and ffprobe reports that track's first packet at -3 against a millisecond tick rather
  /// than at 0. The specification says the value is to be subtracted from a block's timestamp to get
  /// the presentation time, and a reader that ignored it would put every packet of such a track three
  /// milliseconds late — which is inaudible on its own and audible against a picture.
  /// </remarks>
  internal long CodecDelayTicks { get; init; }
}
