using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using FileFormat.Core;
using Hawkynt.FileFormats.Video;

namespace FileFormat.Dfa;

/// <summary>
/// A Chronomaster DFA film: a 128-byte <c>DFIA</c> header, then frame after frame a run of chunks ended
/// by an <c>EOFR</c> chunk.
/// </summary>
/// <remarks>
/// Converted from FFmpeg's <c>libavformat/dfa.c</c>, LGPL-2.1-or-later. The header states the version, the
/// frame count, the picture size and the milliseconds a frame; a frame is every chunk up to and including
/// the header of its <c>EOFR</c>, which is what the decoder is handed. Version 0x100 pictures are drawn
/// with pixels twice as wide as they are high; the version travels to the decoder as two bytes of codec
/// private data, as FFmpeg's extradata.
/// </remarks>
public sealed class DfaContainer : IVideoContainerReader<DfaContainer> {

  internal const uint Tag = 0x41494644; // "DFIA"
  internal const uint EndOfFrame = 0x52464F45; // "EOFR"
  internal const int HeaderLength = 128;
  internal const int ChunkHeaderLength = 12;

  internal static readonly CodecTag VideoCodec = CodecTag.FromCharacters("DFIA");

  public required ReadOnlyMemory<byte> Data { get; init; }
  public required int Version { get; init; }
  public required int Frames { get; init; }
  public required int Width { get; init; }
  public required int Height { get; init; }
  public required int MillisecondsPerFrame { get; init; }

  public static string PrimaryExtension => ".dfa";
  public static string[] FileExtensions => [".dfa"];

  public static bool? MatchesSignature(ReadOnlySpan<byte> header)
    => header.Length >= 4 && BinaryPrimitives.ReadUInt32LittleEndian(header) == Tag ? true : null;

  public static DfaContainer FromSpan(ReadOnlySpan<byte> data) => _Open(data.ToArray());

  public static DfaContainer FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return _Open(data);
  }

  public static DfaContainer FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("DFA file not found.", file.FullName);
    return _Open(File.ReadAllBytes(file.FullName));
  }

  private static DfaContainer _Open(byte[] data) {
    if (data.Length < HeaderLength || BinaryPrimitives.ReadUInt32LittleEndian(data) != Tag)
      throw new NotSupportedException("This file does not open with a 128-byte DFIA header; it is not a Chronomaster DFA film.");
    var span = data.AsSpan();
    var width = BinaryPrimitives.ReadUInt16LittleEndian(span[8..]);
    var height = BinaryPrimitives.ReadUInt16LittleEndian(span[10..]);
    if (width == 0 || height == 0)
      throw new InvalidDataException("This DFA film states a picture with no pixels.");
    var milliseconds = BinaryPrimitives.ReadUInt32LittleEndian(span[12..]);
    return new() {
      Data = data,
      Version = BinaryPrimitives.ReadUInt16LittleEndian(span[4..]),
      Frames = BinaryPrimitives.ReadUInt16LittleEndian(span[6..]),
      Width = width,
      Height = height,
      // FFmpeg takes a rate of zero to mean ten frames a second.
      MillisecondsPerFrame = milliseconds == 0 ? 100 : (int)Math.Min(milliseconds, int.MaxValue),
    };
  }

  public static IReadOnlyList<MediaStreamInfo> Streams(DfaContainer container) {
    ArgumentNullException.ThrowIfNull(container);
    var version = new byte[2];
    BinaryPrimitives.WriteUInt16LittleEndian(version, (ushort)container.Version);
    return [
      new() {
        Index = 0,
        Kind = MediaStreamKind.Video,
        Codec = VideoCodec,
        Handler = VideoCodec,
        Width = container.Width,
        Height = container.Height,
        BitsPerPixel = 8,
        TimeBase = new(container.MillisecondsPerFrame, 1000),
        FrameRate = new(1000, container.MillisecondsPerFrame),
        DeclaredFrameCount = container.Frames,
        CodecPrivateData = version,
      },
    ];
  }

  /// <summary>One packet a frame: its chunks, up to and including the header of the <c>EOFR</c> that ends it.</summary>
  public static IEnumerable<CodedPacket> ReadPackets(DfaContainer container) {
    ArgumentNullException.ThrowIfNull(container);
    var data = container.Data;
    var at = HeaderLength;
    long frame = 0;
    // FFmpeg reads a frame's first chunk header whole or not at all.
    while (data.Length - at >= ChunkHeaderLength) {
      var start = at;
      while (true) {
        if (data.Length - at < ChunkHeaderLength) {
          at = data.Length;
          break;
        }
        var header = data.Span.Slice(at, ChunkHeaderLength);
        var size = BinaryPrimitives.ReadUInt32LittleEndian(header[4..]);
        at += ChunkHeaderLength;
        if (BinaryPrimitives.ReadUInt32LittleEndian(header) == EndOfFrame) {
          // FFmpeg steps over anything an EOFR states it carries, and hands over only its header.
          var end = at;
          at = (int)Math.Min(data.Length, at + (long)size);
          yield return new(0, data[start..end], PresentationTimestamp: frame, DecodeTimestamp: frame, Duration: 1, IsKeyFrame: frame == 0);
          ++frame;
          start = -1;
          break;
        }
        at = (int)Math.Min(data.Length, at + (long)size);
      }
      if (start >= 0 && at > start)
        yield return new(0, data[start..at], PresentationTimestamp: frame, DecodeTimestamp: frame, Duration: 1, IsKeyFrame: frame == 0);
    }
  }

  public static VideoMetadata Metadata(DfaContainer container) {
    ArgumentNullException.ThrowIfNull(container);
    var streams = Streams(container);
    return new() { Streams = [new(streams[0].Index, streams[0].Kind, streams[0].Codec)] };
  }
}

/// <summary>Writes Chronomaster DFA films: the 128-byte header, then each frame's chunks as the encoder made them.</summary>
[VerifiedBy(ConformanceOracle.FFmpeg)]
public sealed class DfaWriter : IVideoContainerWriter<DfaWriter> {

  private readonly MemoryStream _output = new();
  private readonly MediaStreamInfo _video;
  private int _frames;
  private bool _finished;

  private DfaWriter(IReadOnlyList<MediaStreamInfo> streams) {
    ArgumentNullException.ThrowIfNull(streams);
    if (streams.Count != 1 || streams[0].Kind != MediaStreamKind.Video || !streams[0].Codec.EqualsIgnoringCase(DfaContainer.VideoCodec))
      throw new NotSupportedException("A DFA film carries one DFIA video stream and nothing else.");
    this._video = streams[0];
    if (this._video.Width is <= 0 or > 65535 || this._video.Height is <= 0 or > 65535)
      throw new NotSupportedException($"DFA states its picture size in sixteen bits; {this._video.Width}x{this._video.Height} does not fit.");
    this._output.Write(new byte[DfaContainer.HeaderLength]);
  }

  public static string PrimaryExtension => ".dfa";
  public static string[] FileExtensions => [".dfa"];

  public static DfaWriter Create(IReadOnlyList<MediaStreamInfo> streams, VideoMetadata metadata) => new(streams);

  public void WritePacket(CodedPacket packet) {
    if (this._finished)
      throw new InvalidOperationException("The DFA writer has already been finished.");
    if (packet.StreamIndex != 0)
      throw new ArgumentOutOfRangeException(nameof(packet), packet.StreamIndex, "A DFA film has only stream 0.");

    // A frame is whole chunks, the last of them an EOFR header carrying nothing.
    var data = packet.Data.Span;
    var at = 0;
    while (true) {
      if (data.Length - at < DfaContainer.ChunkHeaderLength)
        throw new InvalidDataException("A DFA frame must end with an EOFR chunk header.");
      var size = BinaryPrimitives.ReadUInt32LittleEndian(data[(at + 4)..]);
      if (BinaryPrimitives.ReadUInt32LittleEndian(data[at..]) == DfaContainer.EndOfFrame) {
        if (size != 0 || at + DfaContainer.ChunkHeaderLength != data.Length)
          throw new InvalidDataException("A DFA frame's EOFR chunk ends it and carries nothing.");
        break;
      }
      if (size > (uint)(data.Length - at - DfaContainer.ChunkHeaderLength))
        throw new InvalidDataException($"The DFA chunk at frame byte {at} states {size} bytes, more than remain.");
      at += DfaContainer.ChunkHeaderLength + (int)size;
    }
    this._output.Write(data);
    ++this._frames;
  }

  public byte[] Finish() {
    if (this._finished)
      throw new InvalidOperationException("The DFA writer has already been finished.");
    this._finished = true;
    if (this._frames > 65535)
      throw new NotSupportedException("DFA counts its frames in sixteen bits.");

    var rate = this._video.FrameRate;
    var milliseconds = rate.Numerator > 0 && rate.Denominator > 0
      ? (uint)Math.Clamp(Math.Round(1000.0 * rate.Denominator / rate.Numerator), 1, uint.MaxValue)
      : 100u;
    var version = this._video.CodecPrivateData.Length == 2 ? BinaryPrimitives.ReadUInt16LittleEndian(this._video.CodecPrivateData.Span) : (ushort)0;
    var bytes = this._output.ToArray();
    var header = bytes.AsSpan(0, DfaContainer.HeaderLength);
    BinaryPrimitives.WriteUInt32LittleEndian(header, DfaContainer.Tag);
    BinaryPrimitives.WriteUInt16LittleEndian(header[4..], version);
    BinaryPrimitives.WriteUInt16LittleEndian(header[6..], (ushort)this._frames);
    BinaryPrimitives.WriteUInt16LittleEndian(header[8..], (ushort)this._video.Width);
    BinaryPrimitives.WriteUInt16LittleEndian(header[10..], (ushort)this._video.Height);
    BinaryPrimitives.WriteUInt32LittleEndian(header[12..], milliseconds);
    // Every known film has 0x80 here, the header's length, and FFmpeg's probe trusts a file more for it.
    BinaryPrimitives.WriteUInt32LittleEndian(header[16..], DfaContainer.HeaderLength);
    return bytes;
  }
}
