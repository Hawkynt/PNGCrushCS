using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using FileFormat.Codecs.H265;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>
/// Encodes H.265 / HEVC pictures as independent intra PCM access units.
/// </summary>
/// <remarks>
/// This is deliberately the small end of HEVC rather than a second x265. Every frame is an IDR
/// picture made from 32 by 32 PCM coding units, using the same standards-based encoder already used
/// by the HEIF writer. The coded tools are ordinary Main-profile HEVC: no private escape syntax, no
/// native dependency, and no inter-picture state for a decoder to reconstruct before a packet can be
/// shown.
/// <para/>
/// The shared still-image core labels its parameter sets Main Still Picture, as it should. A video
/// track may carry more than one picture, so this wrapper changes only those profile declarations to
/// Main after the parameter sets have been built. The slice syntax and PCM samples are unchanged;
/// Main contains exactly the 8-bit 4:2:0 tools the core uses.
/// <para/>
/// Samples are carried in the length-prefixed form used by ISO media and Matroska, with the VPS, SPS
/// and PPS in the accompanying <c>HEVCDecoderConfigurationRecord</c>. A raw Annex B writer is a
/// container concern and may translate that representation without changing the coded picture.
/// </remarks>
[VerifiedBy(ConformanceOracle.FFmpeg)]
public sealed class H265VideoEncoder : IVideoCodecEncoder<H265VideoEncoder> {

  private static readonly CodecTag _codec = CodecTag.FromCharacters("hvc1");

  private readonly MediaStreamInfo _requested;
  private MediaStreamInfo? _stream;
  private byte[]? _configuration;
  private H265VideoSequenceEncoder? _sequence;

  private H265VideoEncoder(MediaStreamInfo stream) => this._requested = stream;

  public static string CodecName => H265VideoDecoder.CodecName;

  public static CodecTag Codec => _codec;

  public static H265VideoEncoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.Kind != MediaStreamKind.Video)
      throw new NotSupportedException("H.265 can only encode a video stream.");
    if (stream.Width <= 0 || stream.Height <= 0)
      throw new NotSupportedException(
        $"An H.265 encoder needs the output dimensions before coding begins; {stream.Width}x{stream.Height} was supplied.");
    if ((stream.Width & 1) != 0 || (stream.Height & 1) != 0)
      throw new NotSupportedException(
        $"This H.265 writer codes 4:2:0 pictures, whose conformance-window crop units are two luma samples. "
        + $"{stream.Width}x{stream.Height} cannot therefore be represented exactly without a container clean-aperture property, "
        + "which a codec packet does not own. Use even dimensions rather than silently changing the displayed picture size.");

    return new(stream);
  }

  public bool TryEncode(RawImage frame, long? presentationTimestamp, out CodedPacket packet) {
    ArgumentNullException.ThrowIfNull(frame);
    if (frame.Width != this._requested.Width || frame.Height != this._requested.Height)
      throw new InvalidDataException(
        $"The H.265 encoder was created for {this._requested.Width}x{this._requested.Height} pictures, but received {frame.Width}x{frame.Height}.");
    if (!frame.HasEnoughPixelData)
      throw new InvalidDataException(
        $"A {frame.Width}x{frame.Height} {frame.Format} picture needs {frame.MinimumPixelDataLength} bytes and carries {frame.PixelData.Length}.");

    this._sequence ??= new(frame.Width, frame.Height);
    this._RecordConfiguration();

    if (!this._sequence.TryEncode(frame, presentationTimestamp, out var coded)) {
      // A picture the bidirectional ones may predict from has to be coded before them, so a picture
      // handed in is not always a packet handed back. Flush returns what is still held.
      packet = default;
      return false;
    }

    packet = _ToPacket(this._requested.Index, coded);
    return true;
  }

  /// <summary>Takes the packets the encoder is still holding once the pictures have run out.</summary>
  public IEnumerable<CodedPacket> Flush() {
    if (this._sequence == null)
      yield break;

    foreach (var coded in this._sequence.Flush())
      yield return _ToPacket(this._requested.Index, coded);
  }

  private static CodedPacket _ToPacket(int streamIndex, H265VideoSequenceEncoder.Coded coded)
    => new(
      StreamIndex: streamIndex,
      Data: coded.Sample,
      PresentationTimestamp: coded.PresentationTimestamp,
      DecodeTimestamp: coded.DecodeTimestamp,
      IsKeyFrame: coded.IsKeyFrame);

  private void _RecordConfiguration() {
    var configuration = _AsMainProfile(this._sequence!.Configuration);
    if (this._configuration == null) {
      this._configuration = configuration;
      return;
    }

    if (!configuration.AsSpan().SequenceEqual(this._configuration))
      throw new InvalidDataException(
        "The HEVC parameter sets changed while encoding a fixed-geometry stream. A video stream description can carry only one decoder configuration here.");
  }

  public MediaStreamInfo DescribeStream() {
    if (this._stream != null)
      return this._stream;
    if (this._configuration == null)
      throw new InvalidOperationException(
        "An H.265 stream cannot be described before its parameter sets exist. Encode the first picture first.");

    return this._stream = new() {
      Index = this._requested.Index,
      Kind = MediaStreamKind.Video,
      Codec = _codec,
      Handler = _codec,
      CodecId = "V_MPEGH/ISO/HEVC",
      TimeBase = this._requested.TimeBase,
      FrameRate = this._requested.FrameRate,
      DeclaredFrameCount = this._requested.DeclaredFrameCount,
      Width = this._requested.Width,
      Height = this._requested.Height,
      CodecPrivateData = this._configuration,
      Language = this._requested.Language,
      Name = this._requested.Name,
    };
  }

  /// <summary>
  /// Re-labels the still-image core's parameter sets as Main profile for a multi-picture stream.
  /// </summary>
  /// <remarks>
  /// These offsets are not generic HEVC parsing. They are the fixed prefix emitted by
  /// <see cref="H265PcmStillCodec"/>: the VPS profile-tier-level begins after four RBSP bytes and the
  /// SPS one after a single RBSP byte. The guard bytes deliberately make a future change to that
  /// private encoder fail here rather than silently patching an unrelated field.
  /// </remarks>
  private static byte[] _AsMainProfile(byte[] stillConfiguration) {
    var result = stillConfiguration.AsSpan().ToArray();
    if (result.Length < 23 || result[0] != 1)
      throw new InvalidDataException("The shared HEVC encoder returned no decoder configuration record.");

    result[1] = (byte)((result[1] & 0xE0) | H265ProfileTierLevel.MAIN);
    result[2] = 0x40; // general_profile_compatibility_flag[1], Main only

    var arrays = result[22];
    var at = 23;
    for (var array = 0; array < arrays; ++array) {
      if (at + 3 > result.Length)
        throw new InvalidDataException("The shared HEVC encoder returned a truncated parameter-set array.");

      var type = (H265NalUnitType)(result[at] & 0x3F);
      var count = BinaryPrimitives.ReadUInt16BigEndian(result.AsSpan(at + 1, 2));
      at += 3;

      for (var index = 0; index < count; ++index) {
        if (at + 2 > result.Length)
          throw new InvalidDataException("The shared HEVC encoder returned a truncated parameter-set length.");

        var length = BinaryPrimitives.ReadUInt16BigEndian(result.AsSpan(at, 2));
        at += 2;
        if (at + length > result.Length)
          throw new InvalidDataException("The shared HEVC encoder returned a truncated parameter set.");

        var nal = result.AsSpan(at, length);
        switch (type) {
          case H265NalUnitType.VideoParameterSet:
            _PatchProfile(nal, 6, 7);
            break;
          case H265NalUnitType.SequenceParameterSet:
            _PatchProfile(nal, 3, 4);
            break;
        }

        at += length;
      }
    }

    return result;
  }

  private static void _PatchProfile(Span<byte> nal, int profileByte, int compatibilityByte) {
    if (nal.Length <= compatibilityByte
        || (nal[profileByte] & 0x1F) != H265ProfileTierLevel.MAIN_STILL_PICTURE
        || nal[compatibilityByte] != 0x50)
      throw new InvalidDataException(
        "The shared HEVC encoder's profile-tier-level prefix changed; refusing to rewrite an unknown parameter-set layout.");

    nal[profileByte] = (byte)((nal[profileByte] & 0xE0) | H265ProfileTierLevel.MAIN);
    nal[compatibilityByte] = 0x40;
  }
}
