using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using FileFormat.Core;
using Hawkynt.FileFormats.Video;

namespace FileFormat.Duk;

/// <summary>
/// A Star Control II cutscene from the 3DO release, as The Ur-Quan Masters plays it: four files of one
/// name — <c>.duk</c> holding the chunks, <c>.hdr</c> the picture size and the two delta tables,
/// <c>.frm</c> the offset of every chunk, and <c>.tbl</c> the 256 codebook vectors.
/// </summary>
/// <remarks>
/// Written from a specification derived from NihAV's <c>na_game_tool</c> (GPL-3.0, so read and not
/// converted), which is the oracle the tests compare against. A chunk is a big-endian sound length and
/// picture length, then the sound — Duck's 4-bit stereo ADPCM, handed out as it is — then the picture.
/// The film is 11025/754 pictures a second, the sound 22,050 Hz stereo. The format has no signature, and
/// its parts are separate files, so it opens only from a file, beside which the other three must lie.
/// </remarks>
public sealed class DukContainer : IVideoContainerReader<DukContainer> {

  internal const int HeaderLength = 48;
  internal const int CodebookLength = 256 * 16;

  internal static readonly CodecTag VideoCodec = CodecTag.FromCharacters("UQMV");
  internal static readonly CodecTag AudioCodec = CodecTag.FromCharacters("UQMA");

  public required ReadOnlyMemory<byte> Data { get; init; }
  public required byte[] Header { get; init; }
  public required uint[] Offsets { get; init; }
  public required byte[] Codebook { get; init; }
  public required int Width { get; init; }
  public required int Height { get; init; }

  public static string PrimaryExtension => ".duk";
  public static string[] FileExtensions => [".duk"];

  public static bool? MatchesSignature(ReadOnlySpan<byte> header) => null;

  public static DukContainer FromSpan(ReadOnlySpan<byte> data)
    => throw new NotSupportedException("A Star Control II film is four files — .duk, .hdr, .frm and .tbl — and opens only from a file, beside which the other three lie.");

  public static DukContainer FromBytes(byte[] data) => FromSpan(data);

  /// <summary>Opens the film any of whose four files is <paramref name="file"/>, finding the other three beside it whatever their case.</summary>
  public static DukContainer FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    var directory = file.Directory ?? throw new FileNotFoundException("A Star Control II film's directory cannot be found.", file.FullName);
    var stem = Path.GetFileNameWithoutExtension(file.Name);
    byte[] Part(string extension) {
      foreach (var candidate in directory.EnumerateFiles())
        if (string.Equals(Path.GetFileNameWithoutExtension(candidate.Name), stem, StringComparison.OrdinalIgnoreCase)
            && string.Equals(candidate.Extension, extension, StringComparison.OrdinalIgnoreCase))
          return File.ReadAllBytes(candidate.FullName);
      throw new FileNotFoundException($"A Star Control II film needs its {extension} file beside it.", Path.Combine(directory.FullName, stem + extension));
    }

    return Open(Part(".duk"), Part(".hdr"), Part(".frm"), Part(".tbl"));
  }

  /// <summary>Opens a film from the contents of its four files.</summary>
  internal static DukContainer Open(byte[] duk, byte[] hdr, byte[] frm, byte[] tbl) {
    ArgumentNullException.ThrowIfNull(duk);
    ArgumentNullException.ThrowIfNull(hdr);
    ArgumentNullException.ThrowIfNull(frm);
    ArgumentNullException.ThrowIfNull(tbl);
    if (hdr.Length < HeaderLength)
      throw new InvalidDataException($"A Star Control II .hdr file is {HeaderLength} bytes; this one is {hdr.Length}.");
    var version = BinaryPrimitives.ReadUInt32BigEndian(hdr);
    if (version is not (1 or 3))
      throw new NotSupportedException($"This Star Control II film states version {version}; only 1 and 3 are known.");
    var width = BinaryPrimitives.ReadUInt16BigEndian(hdr.AsSpan(12)) * 8;
    var height = BinaryPrimitives.ReadUInt16BigEndian(hdr.AsSpan(14)) * 8;
    if (width is <= 0 or > 1024 || height is <= 0 or > 768)
      throw new InvalidDataException($"This Star Control II film states a picture of {width}x{height}.");
    if (tbl.Length < CodebookLength)
      throw new InvalidDataException($"A Star Control II .tbl file holds 256 vectors of 16 bytes; this one is {tbl.Length} bytes.");
    if (frm.Length < 4)
      throw new InvalidDataException("This Star Control II .frm file lists no chunk.");

    var offsets = new uint[frm.Length / 4];
    for (var i = 0; i < offsets.Length; ++i)
      offsets[i] = BinaryPrimitives.ReadUInt32BigEndian(frm.AsSpan(4 * i));

    return new() {
      Data = duk,
      Header = hdr[..HeaderLength],
      Offsets = offsets,
      Codebook = tbl[..CodebookLength],
      Width = width,
      Height = height,
    };
  }

  public static IReadOnlyList<MediaStreamInfo> Streams(DukContainer container) {
    ArgumentNullException.ThrowIfNull(container);
    var configuration = new byte[HeaderLength + CodebookLength];
    container.Header.CopyTo(configuration, 0);
    container.Codebook.CopyTo(configuration, HeaderLength);
    return [
      new() {
        Index = 0,
        Kind = MediaStreamKind.Video,
        Codec = VideoCodec,
        Handler = VideoCodec,
        Width = container.Width,
        Height = container.Height,
        BitsPerPixel = 15,
        TimeBase = new(754, 11025),
        FrameRate = new(11025, 754),
        DeclaredFrameCount = container.Offsets.Length,
        CodecPrivateData = configuration,
      },
      new() {
        Index = 1,
        Kind = MediaStreamKind.Audio,
        Codec = AudioCodec,
        TimeBase = new(1, 22050),
        SampleRate = 22050,
        Channels = 2,
        BitsPerSample = 4,
      },
    ];
  }

  /// <summary>Each chunk's sound, then its picture, in the order the <c>.frm</c> file lists the chunks.</summary>
  public static IEnumerable<CodedPacket> ReadPackets(DukContainer container) {
    ArgumentNullException.ThrowIfNull(container);
    var data = container.Data;
    long sample = 0;
    for (var frame = 0; frame < container.Offsets.Length; ++frame) {
      var at = (long)container.Offsets[frame];
      if (at + 8 > data.Length)
        throw new InvalidDataException($"Chunk {frame} of this Star Control II film starts past the end of its .duk file.");
      var sound = BinaryPrimitives.ReadUInt32BigEndian(data.Span[(int)at..]);
      var picture = BinaryPrimitives.ReadUInt32BigEndian(data.Span[(int)(at + 4)..]);
      if (sound <= 10 || picture <= 16 || at + 8 + sound + picture > data.Length)
        throw new InvalidDataException($"Chunk {frame} of this Star Control II film states a sound of {sound} and a picture of {picture} bytes, which its .duk file does not hold.");
      yield return new(1, data.Slice((int)at + 8, (int)sound), PresentationTimestamp: sample, IsKeyFrame: true);
      sample += sound - 10;
      yield return new(0, data.Slice((int)(at + 8 + sound), (int)picture), PresentationTimestamp: frame, DecodeTimestamp: frame, Duration: 1, IsKeyFrame: true);
    }
  }

  public static VideoMetadata Metadata(DukContainer container) {
    ArgumentNullException.ThrowIfNull(container);
    var streams = Streams(container);
    var declared = new MediaStreamMetadata[streams.Count];
    for (var i = 0; i < streams.Count; ++i)
      declared[i] = new(streams[i].Index, streams[i].Kind, streams[i].Codec);
    return new() { Streams = declared };
  }
}

/// <summary>
/// Writes a Star Control II film: <see cref="Finish"/> hands back the <c>.duk</c> file, and the other
/// three — the header and codebook the picture stream carries, and the chunk offsets — are on the
/// writer afterwards, or written beside it by <see cref="WriteFiles"/>.
/// </summary>
/// <remarks>
/// Each picture becomes a chunk with the sound that arrived since the last one, or with a single silent
/// sample where none did, since every chunk carries sound. A film read by <see cref="DukContainer"/>
/// comes back byte for byte.
/// </remarks>
public sealed class DukWriter : IVideoContainerWriter<DukWriter> {

  private static readonly byte[] _Silence = [0xF7, 0x7F, 0, 1, 0, 0, 0, 0, 0, 0, 0];

  private readonly MemoryStream _duk = new();
  private readonly List<uint> _offsets = [];
  private readonly MediaStreamInfo _video;
  private readonly int _audioIndex;
  private byte[]? _pendingSound;
  private bool _finished;

  private DukWriter(MediaStreamInfo video, int audioIndex) {
    this._video = video;
    this._audioIndex = audioIndex;
  }

  public static string PrimaryExtension => ".duk";
  public static string[] FileExtensions => [".duk"];

  public static DukWriter Create(IReadOnlyList<MediaStreamInfo> streams, VideoMetadata metadata) {
    ArgumentNullException.ThrowIfNull(streams);
    MediaStreamInfo? video = null;
    var audio = -1;
    foreach (var stream in streams)
      if (stream.Kind == MediaStreamKind.Video) {
        if (video != null)
          throw new NotSupportedException("A Star Control II film carries one picture stream.");
        if (stream.Codec != DukContainer.VideoCodec)
          throw new NotSupportedException($"A Star Control II film carries {DukContainer.VideoCodec} pictures; stream {stream.Index} is {stream.Codec}.");
        if (stream.CodecPrivateData.Length < DukContainer.HeaderLength + DukContainer.CodebookLength)
          throw new NotSupportedException($"Stream {stream.Index} carries no Star Control II header and codebook to write.");
        video = stream;
      } else if (stream.Kind == MediaStreamKind.Audio) {
        if (audio >= 0 || stream.Codec != DukContainer.AudioCodec)
          throw new NotSupportedException($"A Star Control II film carries at most one {DukContainer.AudioCodec} sound stream.");
        audio = stream.Index;
      }

    return new(video ?? throw new NotSupportedException("A Star Control II film needs a picture stream."), audio);
  }

  public void WritePacket(CodedPacket packet) {
    if (this._finished)
      throw new InvalidOperationException("This Star Control II film is finished.");
    if (packet.StreamIndex == this._audioIndex) {
      if (this._pendingSound != null)
        throw new InvalidDataException("Two Star Control II sound blocks arrived before the picture between them.");
      if (packet.Data.Length <= 10)
        throw new InvalidDataException("A Star Control II sound block is more than its ten bytes of header.");
      this._pendingSound = packet.Data.ToArray();
      return;
    }

    if (packet.StreamIndex != this._video.Index)
      throw new InvalidDataException($"This Star Control II film has no stream {packet.StreamIndex}.");
    if (packet.Data.Length <= 16)
      throw new InvalidDataException("A Star Control II picture is more than its sixteen bytes of header.");

    var sound = this._pendingSound ?? _Silence;
    this._pendingSound = null;
    this._offsets.Add((uint)this._duk.Position);
    Span<byte> lengths = stackalloc byte[8];
    BinaryPrimitives.WriteUInt32BigEndian(lengths, (uint)sound.Length);
    BinaryPrimitives.WriteUInt32BigEndian(lengths[4..], (uint)packet.Data.Length);
    this._duk.Write(lengths);
    this._duk.Write(sound);
    this._duk.Write(packet.Data.Span);
  }

  /// <summary>The <c>.duk</c> file; the other three are then <see cref="Header"/>, <see cref="FrameTable"/> and <see cref="Codebook"/>.</summary>
  public byte[] Finish() {
    if (this._offsets.Count == 0)
      throw new InvalidOperationException("A Star Control II film needs at least one picture.");
    this._finished = true;
    return this._duk.ToArray();
  }

  /// <summary>The <c>.hdr</c> file.</summary>
  public byte[] Header => this._video.CodecPrivateData[..DukContainer.HeaderLength].ToArray();

  /// <summary>The <c>.tbl</c> file.</summary>
  public byte[] Codebook => this._video.CodecPrivateData.Slice(DukContainer.HeaderLength, DukContainer.CodebookLength).ToArray();

  /// <summary>The <c>.frm</c> file: every chunk's offset, big-endian.</summary>
  public byte[] FrameTable {
    get {
      var table = new byte[4 * this._offsets.Count];
      for (var i = 0; i < this._offsets.Count; ++i)
        BinaryPrimitives.WriteUInt32BigEndian(table.AsSpan(4 * i), this._offsets[i]);
      return table;
    }
  }

  /// <summary>Finishes the film and writes its four files, the other three beside <paramref name="duk"/> under its name.</summary>
  public void WriteFiles(FileInfo duk) {
    ArgumentNullException.ThrowIfNull(duk);
    var data = this._finished ? this._duk.ToArray() : this.Finish();
    var stem = Path.Combine(duk.DirectoryName ?? ".", Path.GetFileNameWithoutExtension(duk.Name));
    File.WriteAllBytes(duk.FullName, data);
    File.WriteAllBytes(stem + ".hdr", this.Header);
    File.WriteAllBytes(stem + ".frm", this.FrameTable);
    File.WriteAllBytes(stem + ".tbl", this.Codebook);
  }
}
