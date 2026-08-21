using System;
using System.Collections.Generic;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Asf;

/// <summary>
/// An ASF file taken apart into the streams it declares and the packets it holds — and nothing else.
/// </summary>
/// <remarks>
/// Advanced Systems Format is one format under three extensions: <c>.asf</c> is the format's own name,
/// <c>.wmv</c> is an ASF whose first stream carries pictures and <c>.wma</c> one whose streams carry
/// only sound. Nothing in the file distinguishes them, so nothing here does either — the extension is
/// the writer's hint about what is inside, and what is inside is answered by reading the streams.
/// <para/>
/// This container knows where every packet is and not one thing about what is inside any of them. It
/// does not decode, it does not report pictures, and it does not refuse a file for naming a codec
/// nothing here reads: an ASF full of Windows Media Video 9 is a perfectly good ASF, and copying its
/// packets into another container needs no decoder at all. That is what makes this useful on its own —
/// every <c>.wmv</c> becomes inspectable, and every one of them remuxable, whether or not the codec
/// inside it has a decoder here.
/// <para/>
/// The one thing it does beyond finding bytes is reassembly, which is not decoding. ASF splits a coded
/// frame — it calls one a "media object" — across as many payloads and as many packets as it needs to,
/// because its packets are a fixed size and frames are not. A reader that handed those pieces out
/// separately would be reporting the shape of the wire rather than the shape of the film, and every
/// packet count it produced would disagree with every other tool's.
/// </remarks>
[FormatMagicBytes([0x30, 0x26, 0xB2, 0x75, 0x8E, 0x66, 0xCF, 0x11, 0xA6, 0xD9, 0x00, 0xAA, 0x00, 0x62, 0xCE, 0x6C])]
[FormatMimeType("video/x-ms-asf", "video/x-ms-wmv", "audio/x-ms-wma", "video/x-ms-wm", "application/vnd.ms-asf")]
public sealed class AsfContainer : IVideoContainerReader<AsfContainer> {

  /// <summary>Every stream the file declares, in declaration order.</summary>
  public required IReadOnlyList<MediaStreamInfo> StreamInfos { get; init; }

  /// <summary>What the file says about itself.</summary>
  public required VideoMetadata FileMetadata { get; init; }

  /// <summary>The whole file, as a window rather than a copy.</summary>
  /// <remarks>
  /// The whole of it rather than just the Data Object, because a payload's bytes are handed out as
  /// windows onto this and the offsets that find them are counted from the file's start.
  /// </remarks>
  public required ReadOnlyMemory<byte> File { get; init; }

  /// <summary>Where the Data Object's first packet begins, counted from the file's start.</summary>
  public required int DataStart { get; init; }

  /// <summary>Where the Data Object ends, counted from the file's start.</summary>
  public required int DataEnd { get; init; }

  /// <summary>How many packets the header claimed, or zero for a broadcast, which claimed nothing.</summary>
  public required long PacketCount { get; init; }

  /// <summary>The length of a packet that states none of its own, which is nearly all of them.</summary>
  public required int PacketSize { get; init; }

  /// <summary>How far ahead of real time every timestamp in the file is stated, in milliseconds.</summary>
  public required long Preroll { get; init; }

  /// <summary>Which stream index each ASF stream number belongs to, or -1 for a number nothing declared.</summary>
  /// <remarks>
  /// ASF numbers its streams from one and a file may leave gaps, where a stream's index is its position
  /// among the declarations. The two are different numbers and a payload states the first, so demuxing
  /// needs the translation between them; publishing an index straight out of a payload would number
  /// this container's streams differently from every other one here.
  /// </remarks>
  internal int[] StreamIndexByNumber { get; init; } = [];

  // -------- Format identity --------

  public static string PrimaryExtension => ".asf";

  public static string[] FileExtensions => [".asf", ".wmv", ".wma", ".wm", ".wmx", ".asx"];

  /// <summary>An ASF file begins with the Header Object's identifier and nothing else does.</summary>
  public static bool? MatchesSignature(ReadOnlySpan<byte> header)
    => AsfGuid.Equals(header, AsfGuid.Header) ? true : null;

  // -------- Demux --------

  public static AsfContainer FromSpan(ReadOnlySpan<byte> data) => AsfReader.FromSpan(data);

  /// <summary>Opens an ASF file over the caller's array, keeping it rather than copying it.</summary>
  public static AsfContainer FromBytes(byte[] data) => AsfReader.FromBytes(data);

  public static AsfContainer FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("ASF file not found.", file.FullName);

    return AsfReader.FromBytes(System.IO.File.ReadAllBytes(file.FullName));
  }

  /// <summary>Every stream the container declares — sound and script as well as pictures.</summary>
  public static IReadOnlyList<MediaStreamInfo> Streams(AsfContainer container) {
    ArgumentNullException.ThrowIfNull(container);

    return container.StreamInfos;
  }

  /// <summary>What the container says about itself.</summary>
  public static VideoMetadata Metadata(AsfContainer container) {
    ArgumentNullException.ThrowIfNull(container);

    return container.FileMetadata;
  }

  /// <summary>Walks every packet of the file, in the order its media objects finish.</summary>
  /// <remarks>
  /// Lazy and re-runnable: nothing of the Data Object is touched until a packet is asked for. A media
  /// object that arrived in one piece is handed out as a window onto the file; only one split across
  /// several payloads is copied, and then only because its pieces genuinely are not next to each other.
  /// </remarks>
  public static IEnumerable<CodedPacket> ReadPackets(AsfContainer container) {
    ArgumentNullException.ThrowIfNull(container);

    return _Walk(container, null);
  }

  /// <summary>Walks the packets of one stream, in the order its media objects finish.</summary>
  public static IEnumerable<CodedPacket> ReadPackets(AsfContainer container, int streamIndex) {
    ArgumentNullException.ThrowIfNull(container);

    return _Walk(container, streamIndex);
  }

  /// <summary>A media object being put back together out of the payloads that carry its pieces.</summary>
  private sealed class Pending {
    public byte[] Buffer = [];
    public int Filled;
    public int Size;
    public long PresentationTime;
    public bool IsKeyFrame;
  }

  private static IEnumerable<CodedPacket> _Walk(AsfContainer container, int? onlyStream) {
    var pending = new Dictionary<int, Pending>();

    foreach (var payload in AsfPacketReader.Walk(
               container.File, container.DataStart, container.DataEnd,
               container.PacketCount, container.PacketSize, container.Preroll)) {
      var number = payload.StreamNumber;
      var index = (uint)number < (uint)container.StreamIndexByNumber.Length
        ? container.StreamIndexByNumber[number]
        : -1;

      // A payload for a stream the header never declared has nowhere to go: its index would be a
      // position in a list that has no such entry, and inventing one would renumber every stream after
      // it. ffprobe reports no such stream either.
      if (index < 0)
        continue;

      // A whole media object in one payload, which is the ordinary case for anything small enough to
      // fit. It is handed out as a window onto the file — no copy of the film is made to walk it.
      if (payload.Offset == 0 && (payload.MediaObjectSize <= 0 || payload.MediaObjectSize == payload.Data.Length)) {
        pending.Remove(number);
        if (onlyStream == null || index == onlyStream)
          yield return new(index, payload.Data, payload.PresentationTime, IsKeyFrame: payload.IsKeyFrame);

        continue;
      }

      if (payload.Offset == 0) {
        // A media object cannot be longer than the packets it has to arrive in, and the stated size is
        // four bytes of the file like any other. Believing one that claims two gigabytes would allocate
        // two gigabytes for a frame whose remaining pieces do not exist — a malformed file should cost
        // a refused frame, not the memory of the machine reading it.
        if (payload.MediaObjectSize > container.DataEnd - container.DataStart) {
          pending.Remove(number);
          continue;
        }

        // The first piece states how long the whole object will be, which is the only place that number
        // appears — every later piece states only where it goes.
        pending[number] = new() {
          Buffer = new byte[payload.MediaObjectSize],
          Size = payload.MediaObjectSize,
          PresentationTime = payload.PresentationTime,
          IsKeyFrame = payload.IsKeyFrame,
        };
      }

      if (!pending.TryGetValue(number, out var state))
        // A piece that is not the first of an object nothing here has seen the start of — which is what
        // every stream looks like when reading begins in the middle of a file. There is no way to make
        // a frame of it, and half a frame is not a frame.
        continue;

      // A piece that does not begin where the last one ended means one went missing. What has been
      // collected cannot be completed and cannot be shown, so it is dropped rather than handed on as a
      // frame with a hole in it.
      if (payload.Offset != state.Filled || payload.Offset + payload.Data.Length > state.Size) {
        pending.Remove(number);
        continue;
      }

      payload.Data.Span.CopyTo(state.Buffer.AsSpan(state.Filled));
      state.Filled += payload.Data.Length;

      if (state.Filled < state.Size)
        continue;

      pending.Remove(number);

      // The whole object is due when its first piece said it was, and begins a decode where its first
      // piece said it did — the later pieces carry the same time and say nothing new.
      if (onlyStream == null || index == onlyStream)
        yield return new(index, state.Buffer, state.PresentationTime, IsKeyFrame: state.IsKeyFrame);
    }
  }
}
