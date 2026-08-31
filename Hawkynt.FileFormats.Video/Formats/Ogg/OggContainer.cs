using System;
using System.Collections.Generic;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Ogg;

/// <summary>
/// An Ogg file — <c>.ogg</c>, <c>.ogv</c>, <c>.oga</c>, <c>.opus</c> — taken apart into the logical
/// bitstreams it multiplexes and the packets those hold.
/// </summary>
/// <remarks>
/// Ogg is a framing layer and nothing else. It knows how to cut a stream of packets into pages, how
/// to interleave several such streams into one file, and how to say where a page belongs and how far
/// through its stream it is — and it knows nothing whatever about what is inside a packet. That is
/// unusually pure among containers and it is why this reader is small: there is no sample table to
/// rebuild as in an ISO base media file and no element tree to walk as in Matroska, only pages and
/// the lacing that divides them.
/// <para/>
/// The purity has one cost, and it is the granule position. Every other container states a timestamp;
/// Ogg states a number whose meaning its codec's mapping defines, and the mappings genuinely disagree
/// — Vorbis counts output samples, Opus counts them at 48 kHz whatever the encoder was fed, and
/// Theora packs a keyframe number and an offset into two bit fields of one integer. So a demuxer that
/// reports a timestamp has to know which mapping it is looking at. What it does *not* have to know is
/// how to decode anything, and this one does not: see <see cref="OggCodecMapping"/> for where that
/// line is drawn and why it falls there.
/// <para/>
/// The other thing worth knowing is that the granule position sits at the *end* of a page and belongs
/// to the last packet that finishes on it. Packets before that one on the same page carry no position
/// of their own, and the file states nothing about them. Where a mapping advances by exactly one unit
/// per packet — Theora — they can be counted back from exactly, and they are. Where a packet is worth
/// a block of sound whose length is stated in the codec's own setup data — Vorbis, Opus, FLAC — they
/// cannot be, and this reader reports what the file says rather than a reconstruction: the position
/// the page states becomes the timestamp of the packet that *begins* at it, which is exact, and the
/// packets before it carry none.
/// <para/>
/// Chained files, where a whole second physical bitstream is concatenated onto the end of the first,
/// are not taken apart. The bitstreams of the first link are reported and the pages of any later link
/// are skipped, in the same way a Matroska block naming an undeclared track is skipped.
/// </remarks>
[FormatMimeType("video/ogg", "audio/ogg", "application/ogg", "audio/opus")]
public sealed class OggContainer : IVideoContainerReader<OggContainer> {
  /// <summary>Initializes a new instance of this type.</summary>
  public OggContainer() { }

  /// <summary>The whole file, which every packet is a window onto.</summary>
  public required ReadOnlyMemory<byte> File { get; init; }

  /// <summary>Every logical bitstream the file declares, in the order it declares them.</summary>
  /// <remarks>
  /// Declaration order is the order of the begin-of-stream pages, which Ogg requires to come before
  /// any other page in the file. It is what ffprobe numbers its streams by, and it is stable in a way
  /// the serial numbers are not — those are random.
  /// </remarks>
  internal IReadOnlyList<OggBitstream> Bitstreams { get; init; } = [];

  /// <summary>What the file says about itself, out of the comment headers.</summary>
  public required VideoMetadata FileMetadata { get; init; }

  private Dictionary<uint, int>? _bySerialNumber;

  /// <summary>Which stream index a page's serial number belongs to.</summary>
  private Dictionary<uint, int> _SerialIndex {
    get {
      if (this._bySerialNumber != null)
        return this._bySerialNumber;

      var map = new Dictionary<uint, int>(this.Bitstreams.Count);
      for (var i = 0; i < this.Bitstreams.Count; ++i)
        map.TryAdd(this.Bitstreams[i].SerialNumber, i);

      return this._bySerialNumber = map;
    }
  }

  // -------- Format identity --------

  /// <summary>Gets the primary file extension for this format.</summary>
  public static string PrimaryExtension => ".ogg";

  /// <summary>
  /// Every name the one container goes under.
  /// </summary>
  /// <remarks>
  /// All the same format. Xiph asked in RFC 5334 that <c>.ogg</c> be kept for Vorbis audio and that
  /// files be named for what is in them — <c>.ogv</c> for video, <c>.oga</c> for other audio,
  /// <c>.ogx</c> for anything else — and later gave Opus its own <c>.opus</c> and Speex <c>.spx</c>.
  /// Not one of them is a different container, and a demuxer that refused any of them would refuse a
  /// file it takes apart perfectly for the sake of a name.
  /// </remarks>
  public static string[] FileExtensions => [".ogg", ".ogv", ".oga", ".ogx", ".opus", ".spx"];

  /// <summary>A file that begins with a page's capture pattern.</summary>
  /// <remarks>
  /// Four bytes, and they decide it. Nothing else registered here begins <c>OggS</c>, and what kind
  /// of Ogg it is — video, audio, or a mapping nothing here has heard of — is inside the first
  /// bitstream's identification header rather than at any fixed offset, so it is settled when the
  /// file is opened rather than when it is recognised.
  /// </remarks>
  public static bool? MatchesSignature(ReadOnlySpan<byte> header)
    => header.Length >= 4 && header[..4].SequenceEqual(OggPage.CapturePattern) ? true : null;

  // -------- Demux --------

  /// <summary>Reads an instance from the specified byte span.</summary>
  public static OggContainer FromSpan(ReadOnlySpan<byte> data) => OggReader.FromSpan(data);

  /// <summary>Opens a file over the caller's array, keeping it rather than copying it.</summary>
  public static OggContainer FromBytes(byte[] data) => OggReader.FromBytes(data);

  /// <summary>Reads an instance from the specified file.</summary>
  public static OggContainer FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Ogg file not found.", file.FullName);

    return OggReader.FromBytes(System.IO.File.ReadAllBytes(file.FullName));
  }

  /// <summary>Every logical bitstream the file declares — sound as well as pictures.</summary>
  public static IReadOnlyList<MediaStreamInfo> Streams(OggContainer container) {
    ArgumentNullException.ThrowIfNull(container);

    var result = new MediaStreamInfo[container.Bitstreams.Count];
    for (var i = 0; i < result.Length; ++i)
      result[i] = container.Bitstreams[i].Info;

    return result;
  }

  /// <summary>What the file says about itself.</summary>
  public static VideoMetadata Metadata(OggContainer container) {
    ArgumentNullException.ThrowIfNull(container);

    return container.FileMetadata;
  }

  /// <summary>Walks every packet of every bitstream, in the order the file stores them.</summary>
  /// <remarks>
  /// Storage order, which for a file with sound is the interleaving the writer chose: a run of video
  /// pages, then a run of audio pages far enough ahead that a player reading forwards has both by the
  /// time it needs them. Nothing is merged to recover it — the pages are already in that order and
  /// the walk simply follows them.
  /// <para/>
  /// The header packets are not among them. They are the codec's private data rather than coded
  /// media, they are reported once as <see cref="MediaStreamInfo.CodecPrivateData"/>, and ffprobe
  /// does not count them either: a one-second Theora file of twenty-five frames holds twenty-eight
  /// packets and reports twenty-five.
  /// <para/>
  /// Lazy and re-runnable: nothing of a page is touched until a packet is asked for, and a packet's
  /// data is a window onto the buffer the file was read into except where the packet spans pages and
  /// no such window exists.
  /// </remarks>
  public static IEnumerable<CodedPacket> ReadPackets(OggContainer container) {
    ArgumentNullException.ThrowIfNull(container);

    return _Walk(container, null);
  }

  /// <summary>Walks the packets of one bitstream, in storage order.</summary>
  /// <remarks>
  /// By filtering the walk rather than by seeking, because there is nothing to seek with. Ogg holds
  /// no index of any kind — finding a moment in a file means bisecting it on the granule positions,
  /// which answers "where do I seek to" and not "where is packet <c>n</c>".
  /// </remarks>
  public static IEnumerable<CodedPacket> ReadPackets(OggContainer container, int streamIndex) {
    ArgumentNullException.ThrowIfNull(container);

    if ((uint)streamIndex >= (uint)container.Bitstreams.Count)
      return [];

    return _Walk(container, streamIndex);
  }

  private static IEnumerable<CodedPacket> _Walk(OggContainer container, int? onlyStream) {
    var count = container.Bitstreams.Count;
    var states = new _StreamState[count];
    for (var i = 0; i < count; ++i)
      states[i] = new();

    // Reused across the whole walk rather than allocated per page. A page of one packet is the
    // ordinary case for video and would otherwise cost a list per frame for the length of the film.
    var packets = new List<OggAssembledPacket>();
    var emitted = new List<CodedPacket>();

    foreach (var page in OggPageScanner.Walk(container.File)) {
      // Checksummed before the serial number is looked at, not after. The serial number is four of
      // the bytes the checksum covers, so a page skipped for naming an unknown bitstream might be
      // one whose own name was damaged — and skipping it quietly is exactly the silent loss the
      // checksum exists to prevent.
      page.Verify(container.File);

      // A page belonging to no declared bitstream is a chained link's. It is skipped rather than
      // refused, because the rest of the file is perfectly readable without it.
      if (!container._SerialIndex.TryGetValue(page.SerialNumber, out var streamIndex))
        continue;

      var bitstream = container.Bitstreams[streamIndex];
      var state = states[streamIndex];

      packets.Clear();
      state.Assembler.Split(page, packets);
      if (packets.Count == 0)
        continue;

      _Time(bitstream, state, page, packets, streamIndex, onlyStream, emitted);

      foreach (var packet in emitted)
        yield return packet;
    }
  }

  /// <summary>
  /// Gives the packets that finish on one page their timestamps, and turns them into coded packets.
  /// </summary>
  /// <remarks>
  /// Behind a call rather than inside the walk because it reads packet bytes out of spans, which an
  /// iterator cannot hold across a <c>yield</c>.
  /// </remarks>
  private static void _Time(
    OggBitstream bitstream, _StreamState state, OggPage page, List<OggAssembledPacket> packets,
    int streamIndex, int? onlyStream, List<CodedPacket> emitted) {
    emitted.Clear();

    var mapping = bitstream.Mapping;
    var pagePosition = mapping.PositionOf(page.GranulePosition);

    // Where the mapping advances one unit a packet, every packet on this page is placed exactly, by
    // counting back from the position the page states. A page that finishes packets and states no
    // position is not what any writer produces; carrying the count on from the last one that did
    // costs nothing and keeps the rest of the stream's timing rather than losing it to that page.
    var lastOnPage = mapping.OnePositionPerPacket
      ? pagePosition ?? (state.LastPosition is { } previous ? previous + packets.Count : null)
      : null;

    // Everywhere else the only position the file states for a packet is the one at a page boundary,
    // which is where the next packet to begin starts. Nothing states where the packets after it
    // begin, and nothing here invents one.
    var boundary = mapping.OnePositionPerPacket ? null : state.NextPosition;
    state.NextPosition = null;

    for (var i = 0; i < packets.Count; ++i) {
      var packet = packets[i];
      var position = mapping.OnePositionPerPacket
        ? lastOnPage - (packets.Count - 1 - i)
        : i == 0 ? boundary : null;

      // The header packets are codec-private data and not coded media. Skipped after being assembled
      // rather than before, because their lengths are what put the packet numbering in step: a
      // bitstream's first data packet is its fourth packet for Theora and its third for Opus.
      if (packet.Index < bitstream.HeaderPacketCount)
        continue;

      if (position is { } stated)
        state.LastPosition = stated;

      if (onlyStream != null && streamIndex != onlyStream)
        continue;

      emitted.Add(new(
        streamIndex,
        packet.Data,
        position,
        // Ogg states presentation positions and nothing about decode order. Theora reorders no
        // frames, and for a mapping that did, the order would be in its own bitstream rather than
        // here; ffprobe reports the two equal for every Ogg packet measured.
        position,
        // One frame, where a packet is worth exactly one frame. A block of sound is worth a number
        // of samples the codec's setup data states, which is not this reader's to read.
        mapping.OnePositionPerPacket ? 1 : null,
        mapping.IsKeyFrame(packet.Data.Span)));
    }

    // The page's own position is reached once everything that finishes on it has been consumed, so
    // it is where whatever comes next begins.
    if (pagePosition is { } reached)
      state.NextPosition = reached;
  }

  /// <summary>What a walk remembers about one bitstream between pages.</summary>
  private sealed class _StreamState {

    internal OggPacketAssembler Assembler { get; } = new();

    /// <summary>The position of the last packet that had one, for carrying a count forward.</summary>
    internal long? LastPosition { get; set; }

    /// <summary>The position the next packet to begin starts at, from the last page that stated one.</summary>
    internal long? NextPosition { get; set; }
  }
}
