using System;
using System.Collections.Generic;
using System.IO;
using FileFormat.Core;

namespace FileFormat.MpegTs;

/// <summary>
/// Takes an MPEG-2 transport stream apart: which programs it carries, which PIDs those programs'
/// streams are on, and where each coded unit begins and ends.
/// </summary>
/// <remarks>
/// The format was designed for a broadcast rather than for a file, and everything awkward about
/// reading one follows from that. There is no index, no directory and no header — a receiver is
/// expected to be switched on part way through and to find its way by the tables the stream repeats
/// every hundred milliseconds. So the streams are discovered by walking: the program association
/// table at PID 0 names a PID per program, each of those carries a program map naming the PIDs of
/// that program's streams, and only then is it known which of the multiplex's PIDs carry anything.
/// <para/>
/// A coded unit is not a packet either. A PES packet is cut into 184-byte pieces and spread across
/// however many transport packets it needs, so every packet this reader hands out is assembled from
/// several — which is also why its bytes are a copy rather than a window onto the file, the only one
/// of the containers here that cannot avoid one. The pieces are never contiguous: a four-byte header
/// sits between each pair of them.
/// <para/>
/// Where a unit ends is stated for sound and not for pictures. A PES packet may declare its length,
/// and a muxer does so for sound because a run of audio frames has a length before it is written; for
/// pictures it writes zero, because a coded picture's length is not known until it has been coded,
/// and the packet then runs until the next one starts on the same PID. Both are handled, and the
/// difference is visible in the order the packets come out: a unit is handed over when it is
/// complete, so a video packet appears at the moment the next one begins rather than at the moment it
/// began itself. ffprobe reports the same order for the same file.
/// </remarks>
public static class TransportStreamReader {

  /// <summary>
  /// The seconds one timestamp unit stands for.
  /// </summary>
  /// <remarks>
  /// The 90 kHz system clock, fixed by the standard for every stream of every transport stream, which
  /// is why ffprobe reports <c>1/90000</c> for the sound and the pictures alike.
  /// </remarks>
  private static readonly Rational _TIME_BASE = new(1, 90000);

  public static TransportStreamContainer FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Transport stream file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static TransportStreamContainer FromStream(Stream stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.CanSeek) {
      var data = new byte[stream.Length - stream.Position];
      stream.ReadExactly(data);
      return FromBytes(data);
    }

    using var buffer = new MemoryStream();
    stream.CopyTo(buffer);
    return FromBytes(buffer.ToArray());
  }

  public static TransportStreamContainer FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);

    return _Parse(data);
  }

  /// <summary>
  /// Reads a transport stream out of a span.
  /// </summary>
  /// <remarks>
  /// The bytes are copied once here, and only here. A container has to outlive the call that built
  /// it — its packets are assembled out of the file long afterwards — and a span promises nothing
  /// about how long the memory behind it stays valid. Callers that already hold an array should use
  /// <see cref="FromBytes"/>, which keeps theirs.
  /// </remarks>
  public static TransportStreamContainer FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < TransportPacketScanner.PACKET_SIZE)
      throw new InvalidDataException("Data is too small to hold a transport packet.");

    return _Parse(data.ToArray());
  }

  private static TransportStreamContainer _Parse(byte[] data) {
    if (data.Length < TransportPacketScanner.PACKET_SIZE)
      throw new InvalidDataException("Data is too small to hold a transport packet.");

    var (stride, offset) = TransportPacketScanner.Layout(data);
    var file = new ReadOnlyMemory<byte>(data);
    var (streams, byPid, metadata) = _Describe(file, stride, offset);

    return new() {
      File = file,
      PacketStride = stride,
      FirstPacketOffset = offset,
      StreamInfos = streams,
      StreamByPid = byPid,
      FileMetadata = metadata,
    };
  }

  // ------------------------------------------------------------------------------------------
  // Describing the streams
  // ------------------------------------------------------------------------------------------

  /// <summary>
  /// Walks the multiplex's tables until it knows what every program in it holds.
  /// </summary>
  /// <remarks>
  /// The walk stops as soon as the association table has been read and a map has arrived for every
  /// program it names, which for a file written by a muxer is the third packet. It is a walk rather
  /// than a read of a fixed place because a transport stream states nothing at a fixed place; and it
  /// stops early rather than running to the end because the tables repeat, so everything there is to
  /// learn has been learned by then.
  /// <para/>
  /// A file with no association table at all is refused by name. Reporting no streams for it would be
  /// indistinguishable from a multiplex that really carries none, and the difference matters: the
  /// first is a file this reader could not find its way into and the second is a file with nothing in
  /// it.
  /// </remarks>
  private static (MediaStreamInfo[] Streams, IReadOnlyDictionary<int, int> ByPid, VideoMetadata Metadata) _Describe(
    ReadOnlyMemory<byte> file, int stride, int offset) {
    var assemblers = new Dictionary<int, ProgramTables.Assembler>();
    var programs = new List<(int Program, int Pid)>();
    var mapped = new HashSet<int>();
    var elementary = new List<ElementaryStream>();
    var seen = new HashSet<int>();
    var haveAssociation = false;
    string? serviceName = null, serviceProvider = null;

    foreach (var packet in TransportPacketScanner.Walk(file, stride, offset)) {
      if (packet.TransportError || packet.Scrambling != 0 || !packet.HasPayload)
        continue;

      var isMap = _IsProgramMapPid(programs, packet.Pid);
      if (packet.Pid != TransportPacketScanner.PROGRAM_ASSOCIATION_PID
          && packet.Pid != TransportPacketScanner.SERVICE_DESCRIPTION_PID
          && !isMap)
        continue;

      if (!assemblers.TryGetValue(packet.Pid, out var assembler))
        assemblers[packet.Pid] = assembler = new();

      var section = assembler.Accept(packet);
      if (section == null)
        continue;

      switch (section[0]) {
        case ProgramTables.PROGRAM_ASSOCIATION_TABLE when packet.Pid == TransportPacketScanner.PROGRAM_ASSOCIATION_PID && !haveAssociation:
          haveAssociation = true;
          programs.AddRange(ProgramTables.ProgramMapPids(section));
          break;

        case ProgramTables.SERVICE_DESCRIPTION_TABLE when packet.Pid == TransportPacketScanner.SERVICE_DESCRIPTION_PID && serviceName == null:
          (serviceName, serviceProvider) = ProgramTables.Service(section);
          break;

        case ProgramTables.PROGRAM_MAP_TABLE when isMap && mapped.Add(packet.Pid):
          // Every stream of every program, in the order the tables describe them, and a PID declared
          // twice counted once: a multiplex may carry the same stream in two programs, and it is one
          // stream either way.
          foreach (var stream in ProgramTables.ElementaryStreams(section))
            if (seen.Add(stream.Pid))
              elementary.Add(stream);

          break;
      }

      if (haveAssociation && mapped.Count == programs.Count && programs.Count > 0)
        break;
    }

    if (!haveAssociation)
      throw new InvalidDataException(
        $"No program association table arrived on PID {TransportPacketScanner.PROGRAM_ASSOCIATION_PID}, "
        + "so nothing in this file says which of its PIDs carry streams.");

    if (elementary.Count == 0)
      throw new InvalidDataException(
        $"The program association table names {programs.Count} program(s), but no program map for any of them arrived, "
        + "so nothing says what those programs hold.");

    var streams = new MediaStreamInfo[elementary.Count];
    var byPid = new Dictionary<int, int>(elementary.Count);
    for (var i = 0; i < elementary.Count; ++i) {
      streams[i] = _Stream(elementary[i], i);
      byPid[elementary[i].Pid] = i;
    }

    return (streams, byPid, _Metadata(streams, serviceName, serviceProvider));
  }

  private static bool _IsProgramMapPid(List<(int Program, int Pid)> programs, int pid) {
    foreach (var (_, mapPid) in programs)
      if (mapPid == pid)
        return true;

    return false;
  }

  private static MediaStreamInfo _Stream(ElementaryStream elementary, int index) {
    var registration = ProgramTables.Registration(elementary.Descriptors);

    return new() {
      Index = index,
      Kind = _KindOf(elementary),
      Codec = _CodecOf(elementary.StreamType, registration),
      // The stream type as the table stated it, kept beside the code so a refusal names both. The
      // number is the only thing that was actually in the file.
      Handler = new((uint)elementary.StreamType),
      TimeBase = _TIME_BASE,
      // The descriptors, verbatim, which is the whole of what a transport stream says about a codec
      // that is not inside the elementary stream itself. Everything else a decoder needs — the
      // picture size, the frame rate, the sample rate — is in the coded bytes, which is why none of
      // it is reported here: this is a demuxer, and reading it would mean decoding.
      CodecPrivateData = elementary.Descriptors,
      Language = ProgramTables.Language(elementary.Descriptors),
    };
  }

  private static VideoMetadata _Metadata(MediaStreamInfo[] streams, string? serviceName, string? serviceProvider) {
    var streamMetadata = new MediaStreamMetadata[streams.Length];
    for (var i = 0; i < streams.Length; ++i)
      streamMetadata[i] = new(streams[i].Index, streams[i].Kind, streams[i].Codec, streams[i].Language, streams[i].Name);

    var texts = new List<TextMetadataEntry>();
    if (serviceProvider != null)
      texts.Add(new("Service Provider", serviceProvider));

    // No duration and no creation time. A transport stream states neither anywhere: it has no header
    // to have written them into, and the only clock in it is the program's own, which says when a
    // packet is due rather than how long the recording is. Counting the packets to find out is a walk
    // of the whole file, which is the caller's to ask for and not a header's claim.
    return new() {
      Title = serviceName,
      Streams = streamMetadata,
      TextEntries = texts,
    };
  }

  // ------------------------------------------------------------------------------------------
  // Packets
  // ------------------------------------------------------------------------------------------

  /// <summary>What the walk has gathered of one PID's PES packet so far.</summary>
  private sealed class _Assembly {

    /// <summary>Which of the container's streams this PID is.</summary>
    public int Stream;

    /// <summary>Each piece of the unit, in the order they arrived, as windows onto the file.</summary>
    public readonly List<ReadOnlyMemory<byte>> Pieces = [];

    public int Gathered;
    public bool Active;
    public PesHeader Header;

    /// <summary>Whether the packet that began this unit said the stream may be entered there.</summary>
    public bool RandomAccess;

    /// <summary>The counter the next packet of this PID must carry, or none before the first one.</summary>
    public int? Expected;

    /// <summary>The counter the last packet of this PID carried, for spotting the repeat of one.</summary>
    public int Last = -1;

    public void Reset() {
      this.Pieces.Clear();
      this.Gathered = 0;
      this.Active = false;
    }
  }

  /// <summary>Walks the coded units of a container, optionally of one stream only.</summary>
  internal static IEnumerable<CodedPacket> Walk(TransportStreamContainer container, int? onlyStream) {
    var assemblies = new Dictionary<int, _Assembly>();
    foreach (var (pid, stream) in container.StreamByPid)
      assemblies[pid] = new() { Stream = stream };

    foreach (var packet in TransportPacketScanner.Walk(container.File, container.PacketStride, container.FirstPacketOffset)) {
      if (!assemblies.TryGetValue(packet.Pid, out var assembly))
        continue;

      // Known to be corrupt by whoever handed it over. What is in it is not the stream's bytes, and a
      // unit assembled around it would be a frame with a hole in it reported as a whole one.
      if (packet.TransportError)
        throw new InvalidDataException(
          $"The packet at offset {packet.Offset} on PID {packet.Pid} has its transport error indicator set, so its payload is known to be corrupt.");

      if (packet.Scrambling != 0)
        throw new NotSupportedException(
          $"The packet at offset {packet.Offset} on PID {packet.Pid} is scrambled under key {packet.Scrambling}, so its payload is not the coded bytes. This reader reads streams in the clear only.");

      // An adaptation field on its own — a packet carrying a clock reference and nothing else. It is
      // not part of any unit and, having no payload, does not advance the counter either.
      if (!packet.HasPayload)
        continue;

      if (_Missing(assembly, packet, out var expected)) {
        if (assembly.Active)
          throw new InvalidDataException(
            $"The continuity counter of PID {packet.Pid} steps from {expected} to {packet.ContinuityCounter} at offset {packet.Offset}, "
            + "in the middle of a unit, so at least one packet of it is missing and what is here is part of a unit rather than one.");

        assembly.Reset();
      } else if (packet.ContinuityCounter == assembly.Last) {
        // The standard allows a packet to be sent twice so that a receiver that lost the first still
        // gets it. The repeat carries the same counter and the same bytes; appending it would double
        // 184 bytes in the middle of a unit.
        continue;
      }

      assembly.Last = packet.ContinuityCounter;
      assembly.Expected = (packet.ContinuityCounter + 1) & 0x0F;

      if (packet.PayloadUnitStart) {
        // A unit whose length was never stated ends where the next one begins, and this is that
        // moment. It is why the packets come out in the order they complete rather than the order
        // they start, and why the last one of a file is only reached by the flush below.
        //
        // One that did state a length and has not reached it is a different thing entirely: the next
        // unit has started before this one finished, so bytes of it are missing from somewhere in the
        // middle. Handing it over would hand over a packet padded out with zeroes to the length it
        // claimed.
        if (assembly.Active) {
          var pending = PesReader.PacketLength(assembly.Header);
          if (pending > 0 && assembly.Gathered < pending)
            throw new InvalidDataException(
              $"The unit on PID {packet.Pid} declares {pending} bytes but the next one begins at offset {packet.Offset} "
              + $"after only {assembly.Gathered}, so what is here is part of a unit rather than one.");

          if (_Complete(assembly, out var previous) && _Wanted(previous, onlyStream))
            yield return previous;
        }

        assembly.Reset();
        if (!_Begin(assembly, packet))
          continue;
      } else if (!assembly.Active) {
        // The continuation of a unit whose beginning was not read — the start of a recording that
        // began mid-broadcast, or the tail of a unit already handed over.
        continue;
      } else {
        assembly.Pieces.Add(packet.Payload);
        assembly.Gathered += packet.Payload.Length;
      }

      // A stated length is reached in the middle of a transport packet as often as at the end of one;
      // what follows it there is stuffing, and the unit is done.
      var length = PesReader.PacketLength(assembly.Header);
      if (length > 0 && assembly.Gathered >= length && _Complete(assembly, out var finished)) {
        assembly.Reset();
        if (_Wanted(finished, onlyStream))
          yield return finished;
      }
    }

    // Whatever the last packet of each PID left behind. A unit that never stated its length is
    // complete here, because the thing that would have ended it is the next one and there is none.
    foreach (var assembly in assemblies.Values) {
      if (!assembly.Active)
        continue;

      if (PesReader.PacketLength(assembly.Header) > 0)
        throw new InvalidDataException(
          $"The file ends {assembly.Gathered} bytes into a unit that declares {PesReader.PacketLength(assembly.Header)}, "
          + "so what is left of it is part of a unit rather than one.");

      if (_Complete(assembly, out var last) && _Wanted(last, onlyStream))
        yield return last;
    }
  }

  private static bool _Wanted(CodedPacket packet, int? onlyStream) => onlyStream == null || packet.StreamIndex == onlyStream;

  /// <summary>Whether the counter says a packet of this PID went missing before this one.</summary>
  /// <remarks>
  /// The counter advances for packets with a payload and stands still for the ones without, which is
  /// why it is only consulted here. A writer that states a discontinuity is stating that the jump is
  /// deliberate — a splice, or a recording resumed — and the jump is not treated as loss; a unit
  /// caught in the middle of one is still refused, by the caller, because a unit spliced in half is
  /// no more whole than one a packet fell out of.
  /// </remarks>
  private static bool _Missing(_Assembly assembly, in TransportPacket packet, out int expected) {
    expected = assembly.Expected ?? packet.ContinuityCounter;

    if (assembly.Expected == null || packet.Discontinuity)
      return false;

    return packet.ContinuityCounter != expected && packet.ContinuityCounter != assembly.Last;
  }

  /// <summary>Starts a unit off the packet that says one begins here.</summary>
  private static bool _Begin(_Assembly assembly, in TransportPacket packet) {
    var payload = packet.Payload.Span;

    // Not every PID named by a program map carries PES packets. A private stream may carry sections
    // instead, and those begin with a table id rather than with a packet start code. Nothing is
    // handed over for one rather than its bytes being reported as a frame.
    if (!PesReader.StartsPacket(payload))
      return false;

    if (!PesReader.TryRead(payload, out var header))
      throw new InvalidDataException(
        $"The unit beginning at offset {packet.Offset} on PID {packet.Pid} carries {payload.Length} bytes in its first packet, "
        + "which is fewer than its own header states it spends. No muxer writes a header split across two packets, and reading one "
        + "would mean assembling bytes before knowing how many to assemble.");

    assembly.Header = header;
    assembly.Active = true;

    // The random access indicator is in the adaptation field of this packet, and it is the only
    // thing a transport stream says about whether decoding may begin at a unit. Nothing in the
    // elementary bytes is looked at to decide it — that would be the codec's reading, not the
    // container's, and ffprobe only reports a key frame here because its parser decoded far enough
    // to find one.
    assembly.RandomAccess = packet.RandomAccess;
    assembly.Pieces.Add(packet.Payload);
    assembly.Gathered = packet.Payload.Length;
    return true;
  }

  /// <summary>Turns what has been gathered into a packet, or answers false where it holds no coded bytes.</summary>
  private static bool _Complete(_Assembly assembly, out CodedPacket packet) {
    packet = default;

    var length = PesReader.PayloadLength(assembly.Header, assembly.Gathered);
    if (length <= 0)
      return false;

    // The one copy this container cannot avoid. A unit's bytes are spread across as many transport
    // packets as it needs and a four-byte header sits between every pair of them, so there is no
    // window onto the file that is the unit and nothing else.
    var data = new byte[length];
    var written = 0;
    var skip = assembly.Header.PayloadOffset;
    foreach (var piece in assembly.Pieces) {
      var rest = piece;
      if (skip > 0) {
        var dropped = Math.Min(skip, rest.Length);
        rest = rest[dropped..];
        skip -= dropped;
      }

      if (rest.IsEmpty)
        continue;

      var take = Math.Min(rest.Length, length - written);
      if (take <= 0)
        break;

      rest[..take].Span.CopyTo(data.AsSpan(written));
      written += take;
    }

    // The invariant every refusal above exists to keep. Anything short here would be a packet padded
    // out with zeroes to the length it claimed, which is the one thing a demuxer must never hand back
    // as though it were the coded bytes.
    if (written != length)
      throw new InvalidDataException(
        $"A unit of stream {assembly.Stream} was assembled out of {written} bytes where its header states {length}.");

    packet = new(
      assembly.Stream,
      data,
      assembly.Header.PresentationTimestamp,
      assembly.Header.DecodeTimestamp,
      IsKeyFrame: assembly.RandomAccess);

    return true;
  }

  // ------------------------------------------------------------------------------------------
  // Stream types
  // ------------------------------------------------------------------------------------------

  /// <summary>What a stream carries, from the number the program map names its coding by.</summary>
  /// <remarks>
  /// A stream type of 0x06 is "private data in PES packets" and says nothing at all about the coding.
  /// What it really is has to come from the descriptors beside it, which is where DVB puts its AC-3,
  /// its teletext and its subtitles — so those are looked at, and a private stream with nothing
  /// identifying it is reported as data rather than guessed at.
  /// </remarks>
  private static MediaStreamKind _KindOf(ElementaryStream elementary) {
    switch (elementary.StreamType) {
      case 0x01: // MPEG-1 video
      case 0x02: // MPEG-2 video
      case 0x10: // MPEG-4 part 2 visual
      case 0x1B: // H.264
      case 0x20: // H.264 stereoscopic sub-stream
      case 0x21: // JPEG 2000
      case 0x24: // H.265
      case 0x33: // H.266
      case 0x51: // AV1
        return MediaStreamKind.Video;

      case 0x03: // MPEG-1 audio
      case 0x04: // MPEG-2 audio
      case 0x0F: // AAC in ADTS
      case 0x11: // AAC in LATM
      case 0x1C: // AAC without a transport of its own
      case 0x81: // AC-3
      case 0x82: // DTS
      case 0x83: // Dolby TrueHD
      case 0x84: // enhanced AC-3
      case 0x85: // DTS-HD
      case 0x86: // DTS-HD master audio
      case 0x87: // enhanced AC-3 as ATSC writes it
        return MediaStreamKind.Audio;

      case 0x06: {
        var (audio, subtitle) = ProgramTables.PrivateKind(elementary.Descriptors);
        return audio ? MediaStreamKind.Audio : subtitle ? MediaStreamKind.Subtitle : MediaStreamKind.Data;
      }

      case 0x05: // private sections
      case 0x0B: // DSM-CC
      case 0x0C: // ITU-T H.222.1 type C
        return MediaStreamKind.Data;

      default:
        return MediaStreamKind.Unknown;
    }
  }

  /// <summary>
  /// The four-character code the world names a stream type by.
  /// </summary>
  /// <remarks>
  /// A transport stream numbers its codings the way an FLV does, so the number is translated into the
  /// code the same stream carries in every other container and the number itself is kept as the
  /// stream's handler. Where a registration descriptor is present it wins: a stream type of 0x06 says
  /// only "private", and the four characters in that descriptor are the muxer stating what it really
  /// put there.
  /// <para/>
  /// MPEG audio is deliberately left as its number. Stream types 3 and 4 are "MPEG-1 audio" and
  /// "MPEG-2 audio" whatever layer they turn out to be, the layer is in the frame header and so is
  /// the codec's business, and the codes that exist — <c>.mp3</c> among them — name one layer rather
  /// than the family. Naming the family by one of its members would be stating something the file did
  /// not.
  /// </remarks>
  private static CodecTag _CodecOf(int streamType, uint registration) {
    if (registration != 0)
      return new(registration);

    return streamType switch {
      0x01 => CodecTag.FromCharacters("MPG1"),
      0x02 => CodecTag.FromCharacters("MPG2"),
      0x10 => CodecTag.FromCharacters("MP4V"),
      0x0F or 0x11 or 0x1C => CodecTag.FromCharacters("mp4a"),
      0x1B => CodecTag.FromCharacters("H264"),
      0x24 => CodecTag.FromCharacters("HEVC"),
      0x51 => CodecTag.FromCharacters("AV01"),
      0x81 or 0x87 => CodecTag.FromCharacters("ac-3"),
      _ => new((uint)streamType),
    };
  }
}
