using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using FileFormat.Codecs.Vc1;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>
/// Decodes VC-1 video, SMPTE 421M — the codec Windows Media Video 9 is, under its four-character code
/// <c>WMV3</c>.
/// </summary>
/// <remarks>
/// <b>Intra pictures of the Simple and Main profiles, and nothing else.</b> That is the first rung of
/// the format and it is where this stops. What it covers is the whole of SMPTE 421M 8.1: the picture
/// layer of Figure 13, the predicted coded block pattern of 8.1.2.1, the differentially coded DC of
/// 8.1.3.1 with both of its tables, the three-dimensional run-level AC coding of 8.1.3.4 with all
/// eight coding sets and all three escape modes, DC and AC prediction with the scan each implies, both
/// quantisers, the integer inverse transform of Annex A, and the overlap smoothing of 8.5.1.
/// <para/>
/// The sequence header is not in the bitstream at all. Simple and Main profile state it as the
/// thirty-two bit <c>STRUCT_C</c> of Annex J, which the container carries as the stream's private
/// data — so a Windows Media Video stream cannot be decoded from its packets alone, and the demuxer's
/// habit of handing the codec's private data across untouched is what makes it decodable at all.
/// <para/>
/// <b>What it does not do refuses by name.</b> A predicted picture, a bidirectionally predicted one
/// and a skipped one are each refused as what they are, because every one of them needs motion
/// compensation against a reference this decoder never builds. The Advanced profile is refused at the
/// stream, under its own code <c>WVC1</c>, since it carries a sequence header and an entry point
/// structure of its own inside a byte stream and shares only its block layer with what is here.
/// Multi-resolution coding, range reduction and the in-loop deblocking filter are refused where the
/// stream signals them. There is no <c>catch</c> anywhere that hands back a blank, a copied or a
/// repeated picture: a repeated frame is what a legitimate still passage looks like, and nobody checks
/// a picture that looks like a picture.
/// </remarks>
public sealed class Vc1VideoDecoder : IVideoCodecDecoder<Vc1VideoDecoder> {

  /// <summary>The codes containers name a Simple or Main profile stream with.</summary>
  /// <remarks>
  /// <c>WMV3</c> is what an ASF and an AVI carry; <c>WMV9</c> appears on a few files written by
  /// third-party muxers for the same bitstream.
  /// </remarks>
  private static readonly CodecTag[] _Tags = [
    CodecTag.FromCharacters("WMV3"),
    CodecTag.FromCharacters("WMV9"),
  ];

  /// <summary>The codes that name the Advanced profile, which this refuses by name rather than ignores.</summary>
  private static readonly CodecTag[] _AdvancedTags = [
    CodecTag.FromCharacters("WVC1"),
    CodecTag.FromCharacters("VC-1"),
  ];

  private readonly Vc1SequenceHeader _sequence;
  private readonly int _width;
  private readonly int _height;
  private readonly Vc1PictureDecoder _pictures;

  private Vc1VideoDecoder(Vc1SequenceHeader sequence, int width, int height) {
    this._sequence = sequence;
    this._width = width;
    this._height = height;
    this._pictures = new(sequence, (width + 15) / 16, (height + 15) / 16);
  }

  public static string CodecName => "VC-1 / Windows Media Video 9 (SMPTE 421M, Simple and Main profile intra pictures)";

  public static bool Accepts(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);

    if (stream.Kind != MediaStreamKind.Video)
      return false;

    return _Matches(stream.Codec, _Tags) || _Matches(stream.Codec, _AdvancedTags)
           || stream.CodecId is "V_MS/VFW/FOURCC/WMV3" or "V_VC1";
  }

  /// <summary>
  /// Builds a decoder for one stream, reading its sequence header out of the container's private data.
  /// </summary>
  /// <remarks>
  /// The private data arrives as the container found it, which for both ASF and AVI means a
  /// <c>BITMAPINFOHEADER</c> with the sequence header sitting past its end. The header states its own
  /// length, so stepping over it is the container-independent way to reach what belongs to the codec —
  /// and a stream whose private data is only the four bytes is read just as well.
  /// </remarks>
  public static Vc1VideoDecoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);

    if (_Matches(stream.Codec, _AdvancedTags) || stream.CodecId == "V_VC1")
      throw new NotSupportedException(
        $"Stream {stream.Index} is VC-1 Advanced profile ({stream.Codec}), which states its sequence header and entry "
        + "point inside the bitstream rather than in the container. Only the Simple and Main profiles are read here.");

    // A stream this codec names but whose private data holds no sequence header is one it cannot
    // decode, which the contract for this method makes a refusal rather than a complaint about the
    // bytes: Simple and Main profile put the sequence header nowhere else, so there is nothing to fall
    // back on and nothing to be read later. The refusal names the code, because a caller offered a
    // decoder and refused wants to know which codec went missing.
    Vc1SequenceHeader sequence;
    try {
      sequence = Vc1SequenceHeader.ReadFrom(_SequenceHeaderBytes(stream.CodecPrivateData.Span));
    } catch (InvalidDataException e) {
      throw new NotSupportedException(
        $"Stream {stream.Index} is coded as '{stream.Codec}', but its codec private data is not a Simple or Main profile "
        + $"sequence header: {e.Message} Windows Media Video states that header only in the container, so a stream without "
        + "one cannot be decoded.", e);
    }

    if (sequence.Profile == Vc1Profile.Advanced)
      throw new NotSupportedException(
        $"Stream {stream.Index} states the Advanced profile in its sequence header, which is not read here.");

    if (stream.Width <= 0 || stream.Height <= 0)
      throw new NotSupportedException(
        $"Stream {stream.Index} states a size of {stream.Width}x{stream.Height}. Simple and Main profile VC-1 carries no "
        + "picture size in the bitstream, so the container's is the only one there is.");

    if (sequence.MultiResolution)
      throw new NotSupportedException(
        $"Stream {stream.Index} is coded with multi-resolution coding (MULTIRES), whose pictures are decoded at half "
        + "size and upsampled for display. That is not read here.");

    if (sequence.RangeReduction)
      throw new NotSupportedException(
        $"Stream {stream.Index} is coded with range reduction (RANGERED), which scales every reconstructed sample after "
        + "decoding. That is not read here.");

    if (sequence.LoopFilter)
      throw new NotSupportedException(
        $"Stream {stream.Index} is coded with the in-loop deblocking filter (LOOPFILTER), which is part of the "
        + "reconstruction rather than a postprocess and cannot be left out. That is not read here.");

    return new(sequence, stream.Width, stream.Height);
  }

  /// <summary>A <c>BITMAPINFOHEADER</c>, which is a fixed forty bytes whatever it says about itself.</summary>
  private const int _BITMAP_INFO_HEADER_SIZE = 40;

  /// <summary>The sequence header inside a stream's private data, past the bitmap header if there is one.</summary>
  /// <remarks>
  /// Past a fixed forty bytes and not past the size the header states. The <c>biSize</c> field counts
  /// the codec's own data as well as the structure in both of the containers that carry this — a
  /// Windows Media stream states 44 for a forty-byte header and four bytes of sequence header — so
  /// stepping over what it says steps over the very thing being looked for, and lands on the size
  /// field again. Read as a sequence header, that field's low nibble is a profile number of 2, which
  /// is not a profile at all: the refusal is loud, but only because the reserved bits caught it.
  /// </remarks>
  private static ReadOnlySpan<byte> _SequenceHeaderBytes(ReadOnlySpan<byte> privateData) {
    if (privateData.Length <= _BITMAP_INFO_HEADER_SIZE)
      return privateData;

    // A BITMAPINFOHEADER never states a size below its own, and four bytes of sequence header cannot be
    // mistaken for one: read as a length, the largest a sequence header can state is far past a header.
    var declared = BinaryPrimitives.ReadUInt32LittleEndian(privateData);
    return declared is >= _BITMAP_INFO_HEADER_SIZE and <= 0xFFFF
      ? privateData[_BITMAP_INFO_HEADER_SIZE..]
      : privateData;
  }

  private static bool _Matches(CodecTag codec, CodecTag[] tags) {
    foreach (var tag in tags)
      if (codec.EqualsIgnoringCase(tag))
        return true;

    return false;
  }

  /// <summary>Decodes one packet and hands back the picture it holds.</summary>
  /// <returns><c>false</c> when the packet held no picture at all.</returns>
  public bool TryDecode(CodedPacket packet, out RawImage frame) {
    var data = packet.Data.Span;

    // A Simple or Main profile picture of one byte or fewer is a skipped picture, which is the previous
    // one over again (7.1.1.4). This decoder holds no previous picture, so there is nothing to repeat.
    if (data.Length <= 1) {
      frame = null!;
      return false;
    }

    var picture = this._pictures.Decode(data, default, out _);

    frame = new() {
      Width = this._width,
      Height = this._height,
      Format = PixelFormat.Rgb24,
      PixelData = Vc1ColorConversion.ToRgb24(picture, this._width, this._height),
    };

    return true;
  }

  /// <summary>Nothing is ever held back, so there is nothing left when the packets run out.</summary>
  public IEnumerable<RawImage> Flush() => [];
}
