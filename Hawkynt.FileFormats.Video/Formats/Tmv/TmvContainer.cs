using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using FileFormat.Core;
using Hawkynt.FileFormats.Video;

namespace FileFormat.Tmv;

/// <summary>
/// An 8088flex TMV film: a twelve-byte header, then for every frame a text-mode screen of character
/// and attribute bytes and a block of unsigned 8-bit sound, each pair optionally padded to 512 bytes.
/// </summary>
/// <remarks>
/// Converted from FFmpeg's <c>libavformat/tmv.c</c>, LGPL-2.1-or-later. The frame rate is the sound's: the
/// sample rate times the channels over the samples a frame carries. The sound is handed out as PCM
/// under WAVE format 1 with an 8-bit WAVEFORMATEX, so that it can cross into AVI unchanged.
/// </remarks>
[FormatMimeType("video/x-tmv")]
public sealed class TmvContainer : IVideoContainerReader<TmvContainer> {

  internal const uint Tag = 0x56414D54; // "TMAV"
  internal const int HeaderLength = 12;
  internal const int PaddingFeature = 1;
  internal const int StereoFeature = 2;

  internal static readonly CodecTag VideoCodec = CodecTag.FromCharacters("TMAV");

  public required ReadOnlyMemory<byte> Data { get; init; }
  public required int SampleRate { get; init; }
  public required int AudioChunkSize { get; init; }
  public required int Columns { get; init; }
  public required int Rows { get; init; }
  public required bool Stereo { get; init; }
  public required int Padding { get; init; }

  internal int VideoChunkSize => this.Columns * this.Rows * 2;

  public static string PrimaryExtension => ".tmv";
  public static string[] FileExtensions => [".tmv"];

  public static bool? MatchesSignature(ReadOnlySpan<byte> header)
    => header.Length >= HeaderLength && BinaryPrimitives.ReadUInt32LittleEndian(header) == Tag
       && BinaryPrimitives.ReadUInt16LittleEndian(header[4..]) != 0
       && BinaryPrimitives.ReadUInt16LittleEndian(header[6..]) != 0
       && header[8] == 0 && header[9] != 0 && header[10] != 0
         ? true
         : null;

  public static TmvContainer FromSpan(ReadOnlySpan<byte> data) => _Open(data.ToArray());

  public static TmvContainer FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return _Open(data);
  }

  public static TmvContainer FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("TMV file not found.", file.FullName);
    return _Open(File.ReadAllBytes(file.FullName));
  }

  private static TmvContainer _Open(byte[] data) {
    if (data.Length < HeaderLength || BinaryPrimitives.ReadUInt32LittleEndian(data) != Tag)
      throw new NotSupportedException("This file does not open with TMAV; it is not an 8088flex TMV film.");
    var span = data.AsSpan();
    var sampleRate = BinaryPrimitives.ReadUInt16LittleEndian(span[4..]);
    var chunk = BinaryPrimitives.ReadUInt16LittleEndian(span[6..]);
    if (sampleRate == 0 || chunk == 0)
      throw new InvalidDataException("This TMV film states a sample rate or sound chunk of zero.");
    if (span[8] != 0)
      throw new NotSupportedException($"This TMV film states compression method {span[8]}; TMV defines only 0, uncompressed.");
    int columns = span[9], rows = span[10], features = span[11];
    if (columns == 0 || rows == 0)
      throw new InvalidDataException("This TMV film states a screen with no characters.");
    if ((features & ~(PaddingFeature | StereoFeature)) != 0)
      throw new NotSupportedException($"This TMV film states feature bits {features & ~(PaddingFeature | StereoFeature):X2}, which TMV does not define.");

    var video = columns * rows * 2;
    var padding = (features & PaddingFeature) != 0 ? ((video + chunk + 511) & ~511) - (video + chunk) : 0;
    return new() {
      Data = data,
      SampleRate = sampleRate,
      AudioChunkSize = chunk,
      Columns = columns,
      Rows = rows,
      Stereo = (features & StereoFeature) != 0,
      Padding = padding,
    };
  }

  /// <summary>The frame rate: samples a second over samples a frame, both counting every channel.</summary>
  internal static Rational FrameRateOf(int sampleRate, int channels, int chunk) {
    long numerator = (long)sampleRate * channels, denominator = chunk;
    var divisor = _Gcd(numerator, denominator);
    return new(numerator / divisor, denominator / divisor);
  }

  private static long _Gcd(long a, long b) {
    while (b != 0)
      (a, b) = (b, a % b);
    return a;
  }

  public static IReadOnlyList<MediaStreamInfo> Streams(TmvContainer container) {
    ArgumentNullException.ThrowIfNull(container);
    var channels = container.Stereo ? 2 : 1;
    var rate = FrameRateOf(container.SampleRate, channels, container.AudioChunkSize);
    var frames = (container.Data.Length - HeaderLength) / (container.VideoChunkSize + container.AudioChunkSize + container.Padding);
    return [
      new() {
        Index = 0,
        Kind = MediaStreamKind.Video,
        Codec = VideoCodec,
        Handler = VideoCodec,
        Width = container.Columns * 8,
        Height = container.Rows * 8,
        BitsPerPixel = 8,
        TimeBase = new(rate.Denominator, rate.Numerator),
        FrameRate = rate,
        DeclaredFrameCount = frames,
      },
      new() {
        Index = 1,
        Kind = MediaStreamKind.Audio,
        Codec = new(1),
        TimeBase = new(1, container.SampleRate),
        SampleRate = container.SampleRate,
        Channels = channels,
        BitsPerSample = 8,
        CodecPrivateData = WaveFormat(container.SampleRate, channels),
      },
    ];
  }

  /// <summary>The sixteen bytes of WAVEFORMATEX for unsigned 8-bit PCM.</summary>
  internal static byte[] WaveFormat(int sampleRate, int channels) {
    var format = new byte[16];
    BinaryPrimitives.WriteUInt16LittleEndian(format, 1);
    BinaryPrimitives.WriteUInt16LittleEndian(format.AsSpan(2), (ushort)channels);
    BinaryPrimitives.WriteUInt32LittleEndian(format.AsSpan(4), (uint)sampleRate);
    BinaryPrimitives.WriteUInt32LittleEndian(format.AsSpan(8), (uint)(sampleRate * channels));
    BinaryPrimitives.WriteUInt16LittleEndian(format.AsSpan(12), (ushort)channels);
    BinaryPrimitives.WriteUInt16LittleEndian(format.AsSpan(14), 8);
    return format;
  }

  /// <summary>Video and sound alternately, as FFmpeg's demuxer hands them out, until the file ends.</summary>
  public static IEnumerable<CodedPacket> ReadPackets(TmvContainer container) {
    ArgumentNullException.ThrowIfNull(container);
    var data = container.Data;
    var at = HeaderLength;
    long frame = 0, sample = 0;
    var channels = container.Stereo ? 2 : 1;
    while (at < data.Length) {
      var video = Math.Min(container.VideoChunkSize, data.Length - at);
      yield return new(0, data.Slice(at, video), PresentationTimestamp: frame, DecodeTimestamp: frame, Duration: 1, IsKeyFrame: true);
      at += video;
      ++frame;
      if (at >= data.Length)
        yield break;

      var audio = Math.Min(container.AudioChunkSize, data.Length - at);
      yield return new(1, data.Slice(at, audio), PresentationTimestamp: sample, IsKeyFrame: true);
      sample += audio / channels;
      at += audio + container.Padding;
    }
  }

  public static VideoMetadata Metadata(TmvContainer container) {
    ArgumentNullException.ThrowIfNull(container);
    var streams = Streams(container);
    var declared = new MediaStreamMetadata[streams.Count];
    for (var i = 0; i < streams.Count; ++i)
      declared[i] = new(streams[i].Index, streams[i].Kind, streams[i].Codec);
    return new() { Streams = declared };
  }
}

/// <summary>
/// Writes 8088flex TMV films. Sound is unsigned 8-bit PCM at stream 1, one equal-sized block per frame;
/// without a sound stream the writer supplies silence, four hundred samples a frame, because a TMV film
/// is paced by its sound and always carries some.
/// </summary>
[VerifiedBy(ConformanceOracle.FFmpeg)]
public sealed class TmvWriter : IVideoContainerWriter<TmvWriter> {

  private const int _SILENT_CHUNK = 400;

  private readonly MemoryStream _output = new();
  private readonly int _videoChunk;
  private readonly bool _hasAudio;
  private readonly int _silentChunk;
  private int _audioChunk;
  private bool _expectVideo = true;
  private bool _headerWritten;
  private bool _finished;
  private readonly int _sampleRate;
  private readonly int _channels;
  private readonly int _columns;
  private readonly int _rows;

  private TmvWriter(IReadOnlyList<MediaStreamInfo> streams) {
    ArgumentNullException.ThrowIfNull(streams);
    if (streams.Count is < 1 or > 2 || streams[0].Kind != MediaStreamKind.Video || !streams[0].Codec.EqualsIgnoringCase(TmvContainer.VideoCodec))
      throw new NotSupportedException("TMV needs TMAV text-mode video at stream 0 and optionally 8-bit PCM sound at stream 1.");
    var video = streams[0];
    if (video.Width % 8 != 0 || video.Height % 8 != 0 || video.Width / 8 is < 1 or > 255 || video.Height / 8 is < 1 or > 255)
      throw new NotSupportedException($"A TMV screen is 1 to 255 characters of eight pixels each way; {video.Width}x{video.Height} is not.");
    this._columns = video.Width / 8;
    this._rows = video.Height / 8;
    this._videoChunk = this._columns * this._rows * 2;

    if (streams.Count == 2) {
      var audio = streams[1];
      if (audio.Kind != MediaStreamKind.Audio || (audio.BitsPerSample != 0 && audio.BitsPerSample != 8) || audio.Channels is not (0 or 1 or 2))
        throw new NotSupportedException("TMV sound is unsigned 8-bit PCM, mono or stereo.");
      this._hasAudio = true;
      this._sampleRate = audio.SampleRate;
      this._channels = audio.Channels == 0 ? 1 : audio.Channels;
      if (this._sampleRate is <= 0 or > 65535)
        throw new NotSupportedException("TMV states its sample rate in sixteen bits.");
    } else {
      // Silence paced to the video's frame rate.
      var rate = video.FrameRate.Numerator > 0 && video.FrameRate.Denominator > 0
        ? (double)video.FrameRate.Numerator / video.FrameRate.Denominator
        : 25.0;
      this._channels = 1;
      this._silentChunk = _SILENT_CHUNK;
      this._sampleRate = Math.Clamp((int)Math.Round(rate * _SILENT_CHUNK), 1, 65535);
      this._audioChunk = _SILENT_CHUNK;
    }
  }

  public static string PrimaryExtension => ".tmv";
  public static string[] FileExtensions => [".tmv"];

  public static TmvWriter Create(IReadOnlyList<MediaStreamInfo> streams, VideoMetadata metadata) => new(streams);

  public void WritePacket(CodedPacket packet) {
    if (this._finished)
      throw new InvalidOperationException("The TMV writer has already been finished.");

    if (packet.StreamIndex == 0) {
      if (!this._expectVideo)
        throw new InvalidDataException("TMV alternates one screen and one sound block; two screens arrived in a row.");
      if (packet.Data.Length != this._videoChunk)
        throw new InvalidDataException($"A TMV screen is {this._videoChunk} bytes here; a packet of {packet.Data.Length} arrived.");
      if (!this._hasAudio)
        this._WriteHeader();
      this._output.Write(packet.Data.Span);
      if (this._hasAudio)
        this._expectVideo = false;
      else
        for (var i = 0; i < this._silentChunk; ++i)
          this._output.WriteByte(0x80);
      return;
    }

    if (this._expectVideo)
      throw new InvalidDataException("TMV sound follows the screen it belongs to.");
    if (!this._headerWritten) {
      this._audioChunk = packet.Data.Length;
      if (this._audioChunk is <= 0 or > 65535)
        throw new NotSupportedException("A TMV sound block is 1 to 65535 bytes.");
      this._WriteHeaderAfterFirstScreen();
    } else if (packet.Data.Length != this._audioChunk)
      throw new InvalidDataException($"Every TMV sound block is the same size; this film's is {this._audioChunk} bytes and one of {packet.Data.Length} arrived.");
    this._output.Write(packet.Data.Span);
    this._expectVideo = true;
  }

  private byte[] _Header() {
    var header = new byte[TmvContainer.HeaderLength];
    BinaryPrimitives.WriteUInt32LittleEndian(header, TmvContainer.Tag);
    BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(4), (ushort)this._sampleRate);
    BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(6), (ushort)this._audioChunk);
    header[8] = 0;
    header[9] = (byte)this._columns;
    header[10] = (byte)this._rows;
    header[11] = (byte)(this._channels == 2 ? TmvContainer.StereoFeature : 0);
    return header;
  }

  private void _WriteHeader() {
    if (this._headerWritten)
      return;
    this._output.Write(this._Header());
    this._headerWritten = true;
  }

  // With sound, the header's chunk size is only known once the first block arrives, after the first screen.
  private void _WriteHeaderAfterFirstScreen() {
    var screen = this._output.ToArray();
    this._output.SetLength(0);
    this._output.Write(this._Header());
    this._output.Write(screen);
    this._headerWritten = true;
  }

  public byte[] Finish() {
    if (this._finished)
      throw new InvalidOperationException("The TMV writer has already been finished.");
    if (!this._expectVideo)
      throw new InvalidDataException("A TMV film cannot end between a screen and its sound.");
    this._finished = true;
    if (!this._headerWritten)
      this._WriteHeader();
    return this._output.ToArray();
  }
}
