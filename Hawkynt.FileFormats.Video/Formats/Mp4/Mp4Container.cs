using System;
using System.Collections.Generic;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Mp4;

/// <summary>
/// An ISO base media file — MP4, QuickTime MOV, M4V, 3GP — taken apart into the tracks it declares
/// and the samples they are made of, and nothing else.
/// </summary>
/// <remarks>
/// This container knows where every packet is and not one thing about what is inside any of them. It
/// does not decode, it does not report pictures, and it does not refuse a file for naming a codec
/// nothing here reads: an MP4 full of H.264 is a perfectly good MP4, and copying its packets into
/// another container needs no decoder at all. Refusal by code still happens, at the moment a decoder
/// is asked for.
/// <para/>
/// What makes this container different from the RIFF one is where the packet boundaries live. An AVI
/// stores each packet as a chunk with its own length, so walking the file is walking the packets. An
/// ISO base media file stores nothing of the sort — <c>mdat</c> is an undivided heap of bytes — and
/// every boundary is instead a computation over five tables in <c>stbl</c>. That is why nothing here
/// reads <c>mdat</c> at all and why <see cref="Mp4SampleTable"/> is where the work is.
/// <para/>
/// One reader for four extensions because it is one format under four names. The brands in
/// <c>ftyp</c> differ and the codecs inside differ; the box structure does not.
/// </remarks>
[FormatMimeType("video/mp4", "video/quicktime", "video/x-m4v", "video/3gpp", "video/3gpp2")]
public sealed class Mp4Container : IVideoContainerReader<Mp4Container> {

  /// <summary>The brand from <c>ftyp</c>, where the file states one.</summary>
  public string? MajorBrand { get; init; }

  /// <summary>The whole file, which every packet is a window onto.</summary>
  /// <remarks>
  /// The whole of it rather than a slice, because a sample table's chunk offsets are counted from the
  /// start of the file. Keeping anything less would mean rebasing every offset in every table.
  /// </remarks>
  public required ReadOnlyMemory<byte> File { get; init; }

  /// <summary>Every track the file declares, in declaration order.</summary>
  /// <remarks>
  /// Internal because a track is half sample tables, which are this reader's own bookkeeping and not
  /// something a caller has any use for. What a caller wants of a track is its
  /// <see cref="MediaStreamInfo"/>, and <see cref="Streams"/> is where those come out.
  /// </remarks>
  internal IReadOnlyList<Mp4Track> Tracks { get; init; } = [];

  /// <summary>What the file says about itself.</summary>
  public required VideoMetadata FileMetadata { get; init; }

  // -------- Format identity --------

  public static string PrimaryExtension => ".mp4";

  /// <summary>
  /// Every name the one format goes under.
  /// </summary>
  /// <remarks>
  /// <c>.m4a</c> is here too and is not an oversight: it is the same container carrying only sound,
  /// and a demuxer that refused it would refuse a file it can take apart perfectly for the sake of a
  /// name. Whether anything decodes what comes out is the codec's business, not this one's.
  /// </remarks>
  public static string[] FileExtensions => [".mp4", ".m4v", ".mov", ".qt", ".3gp", ".3g2", ".m4a"];

  /// <summary>
  /// A file whose second box header names one of the types only this format uses.
  /// </summary>
  /// <remarks>
  /// The signature is not at the start of the file. An ISO base media file begins with a box length,
  /// which is four bytes of anything, and the four bytes after it are the type — so a signature for
  /// this format has to be read at offset four, and there is no fixed byte at offset zero to check.
  /// <para/>
  /// <c>ftyp</c> is the one every modern writer emits first, and both reference files begin with it.
  /// The others are what a QuickTime file written before <c>ftyp</c> existed begins with, and a file
  /// that starts straight into <c>moov</c> or <c>mdat</c> is still perfectly readable — so they are
  /// accepted, with the length checked for plausibility because four printable letters at offset four
  /// is otherwise a weak thing to claim a whole format on.
  /// <para/>
  /// <c>free</c> and <c>skip</c> are deliberately not on the list even though a file may legitimately
  /// begin with either. They are filler, they carry nothing that identifies anything, and claiming
  /// every file with those four letters at offset four would be claiming a good deal that is not this
  /// format for the sake of the rare one that is — which can still be reached by its name.
  /// </remarks>
  public static bool? MatchesSignature(ReadOnlySpan<byte> header) {
    if (header.Length < 8)
      return null;

    var size = ((uint)header[0] << 24) | ((uint)header[1] << 16) | ((uint)header[2] << 8) | header[3];
    var type = $"{(char)header[4]}{(char)header[5]}{(char)header[6]}{(char)header[7]}";

    return type switch {
      "ftyp" => true,
      // A box of length 0 runs to the end of the file and one of length 1 states a 64-bit length
      // after the type; both are legitimate here, and anything below a bare header is not a box.
      "moov" or "mdat" or "wide" or "pnot" => size is 0 or 1 or >= 8 ? true : null,
      _ => null,
    };
  }

  // -------- Demux --------

  public static Mp4Container FromSpan(ReadOnlySpan<byte> data) => Mp4Reader.FromSpan(data);

  /// <summary>Opens a file over the caller's array, keeping it rather than copying it.</summary>
  public static Mp4Container FromBytes(byte[] data) => Mp4Reader.FromBytes(data);

  public static Mp4Container FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("MP4 file not found.", file.FullName);

    return Mp4Reader.FromBytes(System.IO.File.ReadAllBytes(file.FullName));
  }

  /// <summary>Every track the container declares — sound and text as well as pictures.</summary>
  public static IReadOnlyList<MediaStreamInfo> Streams(Mp4Container container) {
    ArgumentNullException.ThrowIfNull(container);

    var result = new MediaStreamInfo[container.Tracks.Count];
    for (var i = 0; i < result.Length; ++i)
      result[i] = container.Tracks[i].Info;

    return result;
  }

  /// <summary>What the container says about itself.</summary>
  public static VideoMetadata Metadata(Mp4Container container) {
    ArgumentNullException.ThrowIfNull(container);

    return container.FileMetadata;
  }

  /// <summary>Walks every packet of every track, in the order the file stores them.</summary>
  /// <remarks>
  /// Storage order and not track order, which for a file with sound is the difference between the
  /// order it plays in and a whole track followed by another whole track. It is recovered by merging
  /// the tracks on the offset of the sample each is up to — the tables give each track's samples in
  /// ascending order within the track, and taking the earliest of the fronts at each step gives the
  /// interleaving the writer chose. ffprobe reports the same order for the same file.
  /// <para/>
  /// Lazy and re-runnable, and the merge does not change that: it holds one sample per track and
  /// never a list of them, so a two-hour recording enumerated for its first frame costs one frame per
  /// track rather than a materialised film.
  /// </remarks>
  public static IEnumerable<CodedPacket> ReadPackets(Mp4Container container) {
    ArgumentNullException.ThrowIfNull(container);

    return _Interleave(container);
  }

  /// <summary>Walks the packets of one track, in storage order.</summary>
  /// <remarks>
  /// Straight off that track's own tables rather than by filtering the merged walk, which is the
  /// whole reason this overrides the default: the sample tables are an index, and a caller that wants
  /// one track of a file should not pay for the others' packets to be enumerated and thrown away.
  /// </remarks>
  public static IEnumerable<CodedPacket> ReadPackets(Mp4Container container, int streamIndex) {
    ArgumentNullException.ThrowIfNull(container);

    if ((uint)streamIndex >= (uint)container.Tracks.Count)
      return [];

    return _Walk(container, container.Tracks[streamIndex], streamIndex);
  }

  private static IEnumerable<CodedPacket> _Walk(Mp4Container container, Mp4Track track, int index) {
    foreach (var sample in track.Table.Walk())
      yield return _Packet(container, track, index, sample);
  }

  private static CodedPacket _Packet(Mp4Container container, Mp4Track track, int index, Mp4Sample sample)
    => new(
      index,
      container.File.Slice((int)sample.Offset, sample.Size),
      sample.PresentationTimestamp,
      sample.DecodeTimestamp,
      sample.Duration,
      // A track with no sync sample table is one every sample of which may be decoded from — which is
      // what an all-intra codec like Motion JPEG produces, and what ffprobe reports as a keyframe flag
      // on every packet of such a file.
      sample.IsSync);

  private static IEnumerable<CodedPacket> _Interleave(Mp4Container container) {
    var tracks = container.Tracks;
    if (tracks.Count == 1)
      return _Walk(container, tracks[0], 0);

    return _Merge(container);
  }

  private static IEnumerable<CodedPacket> _Merge(Mp4Container container) {
    var tracks = container.Tracks;
    var fronts = new IEnumerator<Mp4Sample>[tracks.Count];
    var live = new bool[tracks.Count];

    try {
      for (var i = 0; i < tracks.Count; ++i) {
        fronts[i] = tracks[i].Table.Walk().GetEnumerator();
        live[i] = fronts[i].MoveNext();
      }

      while (true) {
        var chosen = -1;
        for (var i = 0; i < tracks.Count; ++i) {
          if (!live[i])
            continue;

          // Ties go to the earlier track, which is the order a writer that put two samples at the
          // same offset would have meant — and two tracks cannot really share an offset, so this only
          // decides files whose tables are wrong.
          if (chosen < 0 || fronts[i].Current.Offset < fronts[chosen].Current.Offset)
            chosen = i;
        }

        if (chosen < 0)
          yield break;

        yield return _Packet(container, tracks[chosen], chosen, fronts[chosen].Current);
        live[chosen] = fronts[chosen].MoveNext();
      }
    } finally {
      foreach (var front in fronts)
        front?.Dispose();
    }
  }
}
