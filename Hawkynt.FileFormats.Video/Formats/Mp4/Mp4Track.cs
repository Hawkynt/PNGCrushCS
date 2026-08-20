using FileFormat.Core;

namespace FileFormat.Mp4;

/// <summary>One track of an ISO base media file: what it is, and where its samples are.</summary>
/// <remarks>
/// The two halves are kept apart because they are answered at different times. <see cref="Info"/> is
/// read out of the headers when the file is opened, costs a few hundred bytes per track and is what
/// a caller asking what a file holds gets. <see cref="Table"/> is not read at all until a packet is
/// asked for — it is the sample tables, still as windows onto the file, and walking them is what
/// produces packets.
/// </remarks>
internal sealed class Mp4Track {

  /// <summary>What the track's headers say about it, with no packet having been read.</summary>
  internal required MediaStreamInfo Info { get; init; }

  /// <summary>The tables that say where the track's samples are.</summary>
  internal required Mp4SampleTable Table { get; init; }
}
