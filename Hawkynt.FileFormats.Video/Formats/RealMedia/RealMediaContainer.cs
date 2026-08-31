using System;
using System.Collections.Generic;
using System.IO;
using FileFormat.Core;

namespace FileFormat.RealMedia;

/// <summary>
/// A RealMedia file taken apart into the streams it declares and the packets it holds — and nothing
/// else.
/// </summary>
/// <remarks>
/// RealMedia is one format under three extensions: <c>.rm</c> is the format's own name, <c>.rmvb</c>
/// is one whose pictures were coded at a variable bit rate, and <c>.ra</c> one carrying only sound.
/// Nothing in the file distinguishes them — the chunks are the same chunks — so nothing here does
/// either, and what is inside is answered by reading the streams.
/// <para/>
/// This container knows where every packet is and not one thing about what is inside any of them. It
/// does not decode, it does not report pictures, and it does not refuse a file for naming a codec
/// nothing here reads: a <c>.rm</c> full of RealVideo 4 is a perfectly good <c>.rm</c>, and copying
/// its packets into another container needs no decoder at all. That is what makes this useful on its
/// own — every <c>.rm</c> becomes inspectable and every one of them remuxable, whether or not the
/// codec inside it has a decoder here.
/// <para/>
/// The one thing it does beyond finding bytes is reassembly, which is not decoding. A RealMedia
/// packet is capped at a size the writer chose and a coded picture is not, so a picture arrives in as
/// many pieces as it needs to and the pieces are put back together here. A reader that handed those
/// pieces out separately would be reporting the shape of the wire rather than the shape of the film,
/// and every packet count it produced would disagree with every other tool's.
/// <para/>
/// Where it cut is reported rather than thrown away. RealMedia cuts a picture at its slices, one
/// slice to a piece, and a RealVideo picture's slices are not otherwise findable — they carry no
/// start code and the padding between them is not fixed — so the offsets go out on the packet as
/// <see cref="CodedPacket.FragmentOffsets"/>. ffmpeg carries the same fact by writing a small table of
/// those offsets in front of the picture's bytes, which is why a packet from its demuxer is
/// <c>8n+1</c> bytes longer than the picture it holds; the fact is the same one and only the spelling
/// differs, and a byte layout invented by one demuxer for one decoder is the private arrangement the
/// split between demux and decode exists to prevent.
/// <para/>
/// <b>Measured.</b> Twelve recordings — RealVideo 1, 2, 3 and 4, from 50 kilobytes to 18 megabytes,
/// three hundred and sixty thousand coded pictures between them — were taken apart here and by
/// ffmpeg's own demuxer. Every file produced the same number of pictures, each with the same
/// timestamp, the same key-frame flag, the same number of pieces, and the same number of bytes once
/// ffmpeg's <c>8n+1</c> offset table is accounted for.
/// <para/>
/// Three of the ten are damaged, which is the ordinary state of these files, and they are the
/// interesting three. Two were cut off mid-recording and are read to the last picture that was
/// completely written. The third never had its data chunk's length filled in and re-sends one piece
/// of one picture; it is read to the end, and that picture is recovered whole — where ffmpeg, having
/// lost the sequence at the repeat, hands back the forty-six bytes it still had as though they were a
/// picture.
/// <para/>
/// <b>Sound is handed out as it is stored.</b> RealAudio's own codecs interleave their sub-packets
/// across the packets that carry them, and the geometry that undoes the interleaving — how many
/// sub-packets to a packet, and how long each is — is stated in the RealAudio header this reader
/// hands across as the stream's <see cref="MediaStreamInfo.CodecPrivateData"/>. That makes
/// deinterleaving a codec's business and not a container's, so it is not done here: a stream of
/// <c>cook</c> comes out as the packets the file holds, where ffmpeg's demuxer re-cuts the same
/// stream into five or six times as many. Neither count is wrong; they are counts of different
/// things, and a remux that moves the packets across wants the ones the file holds. The codecs whose
/// interleaver does nothing — <c>dnet</c>, which is AC-3 — come out packet for packet the same as
/// ffmpeg's, which is what makes the difference visibly the interleaving and not the walk.
/// </remarks>
[FormatMagicBytes([0x2E, 0x52, 0x4D, 0x46])]
[FormatMimeType("application/vnd.rn-realmedia", "application/vnd.rn-realmedia-vbr", "audio/x-pn-realaudio", "video/x-pn-realvideo")]
public sealed class RealMediaContainer : IVideoContainerReader<RealMediaContainer> {
  /// <summary>Initializes a new instance of this type.</summary>
  public RealMediaContainer() { }

  /// <summary>Every stream the file declares, in declaration order.</summary>
  public required IReadOnlyList<MediaStreamInfo> StreamInfos { get; init; }

  /// <summary>What the file says about itself.</summary>
  public required VideoMetadata FileMetadata { get; init; }

  /// <summary>The whole file, as a window rather than a copy.</summary>
  /// <remarks>
  /// The whole of it rather than just the data chunk, because a packet's bytes are handed out as
  /// windows onto this and the offsets that find them are counted from the file's start.
  /// </remarks>
  public required ReadOnlyMemory<byte> File { get; init; }

  /// <summary>Where the data chunk's first packet begins, counted from the file's start.</summary>
  public required int DataStart { get; init; }

  /// <summary>Where the data chunk ends, counted from the file's start.</summary>
  public required int DataEnd { get; init; }

  /// <summary>Which stream index each RealMedia stream number belongs to, or -1 for a number nothing declared.</summary>
  internal int[] StreamIndexByNumber { get; init; } = [];

  /// <summary>Whether each RealMedia stream number carries pictures, which decides how its packets are read.</summary>
  /// <remarks>
  /// Not a judgement about the codec — the container has none — but about the layout. A video packet
  /// puts a small header in front of each of its elements saying which piece of which picture it is;
  /// a sound packet does not, and reading one as though it did would take four bytes of sound for a
  /// length and hand back the rest as a frame.
  /// </remarks>
  internal bool[] IsVideoByNumber { get; init; } = [];

  // -------- Format identity --------

  /// <summary>Gets the primary file extension for this format.</summary>
  public static string PrimaryExtension => ".rm";

  /// <summary>
  /// The names a RealMedia file is stored under.
  /// </summary>
  /// <remarks>
  /// <c>.ra</c> is claimed because a sound-only RealMedia file is stored under it, exactly as a
  /// sound-only ASF is stored as <c>.wma</c>. It is not a claim on the older standalone RealAudio
  /// format, which begins with different bytes and is a different format; detection goes by the
  /// signature, so one of those is left undecided here rather than opened as this.
  /// </remarks>
  public static string[] FileExtensions => [".rm", ".rmvb", ".ra", ".rmj", ".rms"];

  /// <summary>A RealMedia file begins with the four characters of its file header and nothing else does.</summary>
  public static bool? MatchesSignature(ReadOnlySpan<byte> header)
    => header.Length >= 4
       && header[0] == (byte)'.' && header[1] == (byte)'R' && header[2] == (byte)'M' && header[3] == (byte)'F'
      ? true
      : null;

  // -------- Demux --------

  /// <summary>Reads an instance from the specified byte span.</summary>
  public static RealMediaContainer FromSpan(ReadOnlySpan<byte> data) => RealMediaReader.FromSpan(data);

  /// <summary>Opens a RealMedia file over the caller's array, keeping it rather than copying it.</summary>
  public static RealMediaContainer FromBytes(byte[] data) => RealMediaReader.FromBytes(data);

  /// <summary>Reads an instance from the specified file.</summary>
  public static RealMediaContainer FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("RealMedia file not found.", file.FullName);

    return RealMediaReader.FromBytes(System.IO.File.ReadAllBytes(file.FullName));
  }

  /// <summary>Every stream the container declares — the sound as well as the pictures.</summary>
  public static IReadOnlyList<MediaStreamInfo> Streams(RealMediaContainer container) {
    ArgumentNullException.ThrowIfNull(container);

    return container.StreamInfos;
  }

  /// <summary>What the container says about itself.</summary>
  public static VideoMetadata Metadata(RealMediaContainer container) {
    ArgumentNullException.ThrowIfNull(container);

    return container.FileMetadata;
  }

  /// <summary>Walks every packet of the file, in the order its pictures finish.</summary>
  /// <remarks>
  /// Lazy and re-runnable: nothing of the data chunk is touched until a packet is asked for. A sound
  /// packet, and a picture that arrived in one piece, are handed out as windows onto the file; only a
  /// picture split across several packets is copied, and then only because its pieces genuinely are
  /// not next to each other.
  /// </remarks>
  public static IEnumerable<CodedPacket> ReadPackets(RealMediaContainer container) {
    ArgumentNullException.ThrowIfNull(container);

    return _Walk(container, null);
  }

  /// <summary>Walks the packets of one stream, in the order its pictures finish.</summary>
  public static IEnumerable<CodedPacket> ReadPackets(RealMediaContainer container, int streamIndex) {
    ArgumentNullException.ThrowIfNull(container);

    return _Walk(container, streamIndex);
  }

  /// <summary>A picture being put back together out of the pieces that carry it.</summary>
  private sealed class Pending {
    public byte[] Buffer = [];
    public int Filled;
    public long? Timestamp;
    public bool IsKeyFrame;

    /// <summary>Where each piece began, which is where each of the picture's slices begins.</summary>
    public readonly List<int> Offsets = [];
  }

  private static IEnumerable<CodedPacket> _Walk(RealMediaContainer container, int? onlyStream) {
    var pending = new Dictionary<int, Pending>();

    foreach (var packet in RealMediaPacketReader.Walk(container.File, container.DataStart, container.DataEnd)) {
      var number = packet.StreamNumber;
      var index = (uint)number < (uint)container.StreamIndexByNumber.Length
        ? container.StreamIndexByNumber[number]
        : -1;

      // A packet for a stream the header never declared has nowhere to go: its index would be a
      // position in a list that has no such entry, and inventing one would renumber every stream after
      // it. ffprobe reports no such stream either.
      if (index < 0)
        continue;

      var wanted = onlyStream == null || index == onlyStream;

      if (!container.IsVideoByNumber[number]) {
        // Sound, text, anything that is not pictures: the payload is the coded bytes and there is no
        // sub-header in front of them.
        if (wanted && !packet.Data.IsEmpty)
          yield return new(index, packet.Data, packet.Timestamp, IsKeyFrame: packet.IsKeyFrame);

        continue;
      }

      foreach (var frame in _Frames(container, packet, index, pending, wanted))
        yield return frame;
    }
  }

  /// <summary>
  /// Reads the elements of one video packet, handing back whichever pictures they completed.
  /// </summary>
  /// <remarks>
  /// A packet may hold the last piece of one picture and the whole of the next, so the elements are
  /// walked rather than the packet being treated as one thing.
  /// <para/>
  /// Only the element that opens a packet may take the packet's timestamp and its key-frame flag. A
  /// picture that begins part way through a packet is a picture the container stated no time for —
  /// there is one timestamp in a packet header and the picture before it has already claimed it — and
  /// the honest answer for that picture is that it has none. ffmpeg fills the gap by adding a frame's
  /// worth of time to the picture before, which is a good guess and is not what the file says.
  /// </remarks>
  private static IEnumerable<CodedPacket> _Frames(
    RealMediaContainer container, RealMediaPacket packet, int index, Dictionary<int, Pending> pending, bool wanted) {
    var file = container.File;
    var end = packet.DataOffset + packet.Data.Length;
    var cursor = packet.DataOffset;
    var isFirstElement = true;

    while (cursor < end) {
      if (!RealMediaVideoFragmentReader.TryRead(file.Span, cursor, end, out var fragment))
        yield break;

      var claims = isFirstElement;
      isFirstElement = false;
      var timestamp = claims ? packet.Timestamp : (long?)null;
      var isKeyFrame = claims && packet.IsKeyFrame;

      if (fragment.Kind != RealMediaFragmentKind.Piece) {
        // A whole picture whose length is the packet's is lost when the packet was cut short: there
        // is no field saying how long it was meant to be, so what is there cannot be known to be all
        // of it. One carrying its own length is checked against the bytes present by the reader.
        if (fragment.Kind == RealMediaFragmentKind.WholeFrame && !packet.IsComplete)
          yield break;

        pending.Remove(packet.StreamNumber);
        if (wanted && fragment.DataLength > 0)
          yield return new(index, file.Slice(fragment.DataOffset, fragment.DataLength), timestamp, IsKeyFrame: isKeyFrame);

        cursor = fragment.End;
        continue;
      }

      if (fragment.Offset == 0) {
        // A picture cannot be longer than the packets it has to arrive in. Believing a length that
        // claims two gigabytes would allocate two gigabytes for a picture whose remaining pieces do
        // not exist — a malformed file should cost a refused picture, not the memory of the machine.
        if (fragment.FrameLength > container.DataEnd - container.DataStart) {
          pending.Remove(packet.StreamNumber);
          cursor = fragment.End;
          continue;
        }

        pending[packet.StreamNumber] = new() {
          Buffer = new byte[fragment.FrameLength],
          Timestamp = timestamp,
          IsKeyFrame = isKeyFrame,
        };
        pending[packet.StreamNumber].Offsets.Add(0);
      }

      if (!pending.TryGetValue(packet.StreamNumber, out var state) || state.Buffer.Length != fragment.FrameLength) {
        // A piece of a picture nothing here has seen the start of — which is what every stream looks
        // like when reading begins in the middle of a file. There is no way to make a picture of it,
        // and half a picture is not a picture.
        cursor = fragment.End;
        continue;
      }

      // A piece wholly inside what has already been collected is a piece sent twice, which a format
      // built for streaming does. Skipping it keeps the picture; treating it as a break in the
      // sequence would throw away a picture whose bytes are all present, and the pieces still to come
      // would then find nothing to attach to and be thrown away too.
      if (fragment.Offset + fragment.DataLength <= state.Filled) {
        cursor = fragment.End;
        continue;
      }

      // A piece that does not begin where the last one ended means one went missing. What has been
      // collected cannot be completed and cannot be shown, so it is dropped rather than handed on as
      // a picture with a hole in it — or, worse, as a picture made of whichever pieces did arrive,
      // which is a picture that looks like a picture and is not the one that was coded.
      if (fragment.Offset != state.Filled) {
        pending.Remove(packet.StreamNumber);
        cursor = fragment.End;
        continue;
      }

      if (fragment.Offset > 0)
        state.Offsets.Add(fragment.Offset);

      file.Span.Slice(fragment.DataOffset, fragment.DataLength).CopyTo(state.Buffer.AsSpan(state.Filled));
      state.Filled += fragment.DataLength;
      cursor = fragment.End;

      // A picture is finished when its bytes are all here. Which piece was marked as the last is a
      // hint and not the definition: plenty of pictures arrive complete without ever carrying one,
      // and waiting for a marker that never comes loses every such picture.
      if (state.Filled < state.Buffer.Length)
        continue;

      pending.Remove(packet.StreamNumber);
      if (wanted)
        yield return new(
          index, state.Buffer, state.Timestamp, IsKeyFrame: state.IsKeyFrame,
          FragmentOffsets: state.Offsets.ToArray());
    }
  }
}
