using System;
using System.Collections.Generic;
using System.IO;
using FileFormat.Codecs.Roq;
using FileFormat.Core;
using FileFormat.RoqVideo;

namespace FileFormat.Codecs;

/// <summary>
/// Encodes id RoQ (<c>RoQV</c>): vector quantisation with motion compensation over a quadtree of 8x8,
/// 4x4 and 2x2 blocks, with a codebook restated whenever the picture needs different cells.
/// </summary>
/// <remarks>
/// The mirror of <see cref="RoqVideoDecoder"/>, and written against it. Nothing here is adapted from
/// another encoder: the bitstream this writes is the bitstream that decoder reads, and that decoder is
/// measured sample for sample against ffmpeg's own over three real films and 1338 pictures, so the walk
/// it implements is the walk the format defines rather than one reading of a description.
/// <para/>
/// <b>A picture is more than one chunk.</b> Cinepak's frame is one chunk and Microsoft Video 1's is one
/// packet, but a RoQ picture is a <c>QUAD_VQ</c> chunk, the <c>QUAD_CODEBOOK</c> chunk it needs where the
/// cells it names have changed, and — once, at the start of the film — an <c>INFO</c> chunk stating the
/// picture size. So one packet here carries the chunks one picture is made of, header and all, exactly as
/// they are to appear in the file, and <see cref="RoqWriter"/> writes them out unchanged. Reading such a
/// file back splits them into one packet a chunk again, which is what the demuxer has always done.
/// <para/>
/// <b>Lossy by construction.</b> RoQ has no lossless form. A 2x2 cell states one chrominance pair for
/// four pixels and every block is painted from a codebook of at most 256 of them, so nothing survives
/// exactly but a picture the codebook can hold outright — one of at most 256 distinct 2x2 cells arranged
/// in at most 256 distinct 4x4 patterns. Such a picture does come back sample for sample; anything richer
/// is quantised, which is the format and not a shortcut here.
/// <para/>
/// <b>What it refuses.</b> A picture whose sides are not a whole number of 16-pixel macroblocks, which
/// is what RoQ codes and the only size its own decoder reads; a picture larger than the two bytes
/// <c>INFO</c> states each side in; and a picture size that changes part way through a stream, since the
/// buffer a skipped block leaves showing is the size it was.
/// <para/>
/// <b>Measured against ffmpeg.</b> Eleven sequences — 64x48 to 512x512, 118 pictures — were encoded here
/// and decoded by ffmpeg 9.0.1, whose <c>yuvj444p</c> planes are identical to this package's own decode of
/// the same files, sample for sample: 0 differing of 18006528. The planes and not colour, because an RGB
/// comparison would be comparing two colour matrices as much as two codecs. How the output compares in
/// size and in error with what ffmpeg's own <c>roqvideo</c> encoder writes from the same source planes is
/// in <c>Hawkynt.FileFormats.Video/codec-notes.md</c>.
/// </remarks>
[VerifiedBy(ConformanceOracle.FFmpeg)]
public sealed class RoqVideoEncoder : IVideoCodecEncoder<RoqVideoEncoder> {

  private static readonly CodecTag _ROQV = CodecTag.FromCharacters("RoQV");

  /// <summary>The side of a macroblock, which is the granularity a picture size has to be a whole
  /// number of.</summary>
  private const int _MACROBLOCK = 16;

  private const int _CHUNK_HEADER_LENGTH = 8;
  private const int _INFO_PAYLOAD_LENGTH = 8;

  /// <summary>The two fields of <c>INFO</c> every file measured states as eight and four, and which the
  /// reader here refuses anything else in.</summary>
  private const ushort _INFO_MACROBLOCK = 8;

  private const ushort _INFO_SUBBLOCK = 4;

  /// <summary>The largest picture <c>INFO</c> can state a side of.</summary>
  private const int _LARGEST_SIDE = ushort.MaxValue / _MACROBLOCK * _MACROBLOCK;

  /// <summary>
  /// How many units of squared sample error one bit of output is worth.
  /// </summary>
  /// <remarks>
  /// The same shape of rate control Cinepak's encoder here uses, but not the same number and not the
  /// same quantity: there the error is measured over forty-eight colour channels of a 4x4 block, here
  /// over forty-eight samples of one — sixteen luminances and thirty-two chrominances, this format's
  /// motion compensation having left the chrominance at full resolution. Two was chosen by measurement
  /// against ffmpeg's own encoder. Swept at 2, 4, 8, 16 and 32 over five sequences, every value writes
  /// less than ffmpeg does on all five, so the choice is not between being smaller and not — it is how
  /// much quality to give up for the rest of the saving, and past two the answer is too much. On a
  /// 128x96 colour grid: two gives 54.51 dB in 3018 bytes against ffmpeg's 53.19 in 3244, four gives
  /// 50.60 in 2412, and sixteen 47.39 in 1568.
  /// </remarks>
  private const int _BIT_COST = 2;

  private readonly MediaStreamInfo _requested;
  private readonly int _width;
  private readonly int _height;
  private readonly RoqPictureEncoder _picture;
  private readonly RoqFrame _source;
  private readonly RoqFrame _bufferA;
  private readonly RoqFrame _bufferB;
  private readonly List<byte> _codebook = [];
  private readonly List<byte> _vectors = [];

  private MediaStreamInfo? _stream;
  private bool _nextTargetIsA = true;
  private bool _hasEncodedFirstPicture;

  private RoqVideoEncoder(MediaStreamInfo stream) {
    this._requested = stream;
    this._width = stream.Width;
    this._height = stream.Height;
    this._picture = new(stream.Width, stream.Height, _BIT_COST);
    this._source = new(stream.Width, stream.Height);
    this._bufferA = new(stream.Width, stream.Height);
    this._bufferB = new(stream.Width, stream.Height);
  }

  public static string CodecName => "id RoQ";

  public static CodecTag Codec => _ROQV;

  /// <summary>
  /// Builds an encoder for the stream described, refusing a size the coding has no form for.
  /// </summary>
  /// <remarks>
  /// RoQ codes nothing but whole 16-pixel macroblocks and states nowhere what a partial one covers, so a
  /// size that is not a whole number of them is refused rather than padded to one — this package's own
  /// decoder refuses to read such a file, and so does ffmpeg's muxer refuse to write one.
  /// </remarks>
  public static RoqVideoEncoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.Kind != MediaStreamKind.Video)
      throw new NotSupportedException("RoQ can only encode a video stream.");
    if (stream.Width <= 0 || stream.Height <= 0)
      throw new NotSupportedException(
        $"A RoQ encoder needs the picture size up front; {stream.Width}x{stream.Height} was supplied.");
    if (stream.Width % _MACROBLOCK != 0 || stream.Height % _MACROBLOCK != 0)
      throw new NotSupportedException(
        $"A picture of {stream.Width}x{stream.Height} is not a whole number of {_MACROBLOCK}-pixel macroblocks. "
        + "RoQ codes nothing but whole macroblocks and states nowhere what a partial one covers.");
    if (stream.Width > _LARGEST_SIDE || stream.Height > _LARGEST_SIDE)
      throw new NotSupportedException(
        $"A picture of {stream.Width}x{stream.Height} is larger than the {_LARGEST_SIDE} pixels a RoQ_INFO chunk "
        + "states each side of a picture in.");

    return new(stream);
  }

  /// <summary>
  /// Codes one picture against the buffers the decoder will hold, and hands back the chunks it is
  /// made of.
  /// </summary>
  /// <remarks>
  /// Always produces a packet, and flags it as a key frame when nothing in it skipped or moved — the two
  /// codings that make a picture depend on what came before. The first picture is coded that way on
  /// purpose, both buffers being empty; after it, nothing forces one, because RoQ has no key-frame
  /// marker of its own and every player of this format starts a film at its beginning.
  /// </remarks>
  public bool TryEncode(RawImage frame, long? presentationTimestamp, out CodedPacket packet) {
    ArgumentNullException.ThrowIfNull(frame);
    if (frame.Width != this._width || frame.Height != this._height)
      throw new InvalidDataException(
        $"RoQ geometry is fixed at {this._width}x{this._height}; received {frame.Width}x{frame.Height}.");
    if (!frame.HasEnoughPixelData)
      throw new InvalidDataException(
        "The source RawImage does not contain enough pixel data for its declared format and dimensions.");

    this._Load(frame);

    var target = this._nextTargetIsA ? this._bufferA : this._bufferB;
    var reference = this._nextTargetIsA ? this._bufferB : this._bufferA;
    var intra = !this._hasEncodedFirstPicture;

    this._codebook.Clear();
    this._vectors.Clear();
    this._picture.Encode(this._source, reference, target, intra, this._codebook, this._vectors);

    var chunks = new List<byte>(
      this._codebook.Count + this._vectors.Count + 3 * _CHUNK_HEADER_LENGTH + _INFO_PAYLOAD_LENGTH);
    if (!this._hasEncodedFirstPicture)
      this._WriteInfo(chunks);

    if (this._codebook.Count > 0)
      _WriteChunk(chunks, RoqChunkType.QUAD_CODEBOOK, _CodebookArgument(this._picture.Cb2Count, this._picture.Cb4Count), this._codebook);

    // The mean motion vector every block's own nibbles are offset from. Nought: a vector this encoder
    // states is the whole vector, and a mean that is not nought only moves where the sixteen-by-sixteen
    // window sits without widening it.
    _WriteChunk(chunks, RoqChunkType.QUAD_VQ, 0, this._vectors);

    if (!this._hasEncodedFirstPicture) {
      // The very first picture has no second buffer to have been building into two pictures ago, so its
      // result becomes both buffers' content — the same bootstrap the decoder performs.
      reference.CopyFrom(target);
      this._hasEncodedFirstPicture = true;
    }

    this._nextTargetIsA = !this._nextTargetIsA;

    packet = new(
      this._requested.Index,
      chunks.ToArray(),
      PresentationTimestamp: presentationTimestamp,
      DecodeTimestamp: presentationTimestamp,
      IsKeyFrame: this._picture.IsWholePicture);
    return true;
  }

  /// <summary>The stream as a muxer needs it described.</summary>
  /// <remarks>
  /// The rate is fixed at thirty pictures a second, and not taken from what the caller asked for,
  /// because a RoQ file has no field to state any other: <see cref="RoqContainer"/> reads every file as
  /// thirty, so writing a stream that claimed otherwise would be writing a number no reader can see.
  /// There is no codec private data either — a RoQ picture states its own size in its own
  /// <c>INFO</c> chunk and needs nothing from the container.
  /// </remarks>
  public MediaStreamInfo DescribeStream()
    => this._stream ??= new() {
      Index = this._requested.Index,
      Kind = MediaStreamKind.Video,
      Codec = _ROQV,
      Handler = _ROQV,
      TimeBase = new Rational(1, 30),
      FrameRate = new Rational(30, 1),
      DeclaredFrameCount = this._requested.DeclaredFrameCount,
      Width = this._width,
      Height = this._height,
      Language = this._requested.Language,
      Name = this._requested.Name,
    };

  // ============================================================================================
  // What the picture is read as
  // ============================================================================================

  /// <summary>
  /// Puts one picture into the samples RoQ states, without converting what is already in them.
  /// </summary>
  /// <remarks>
  /// A <c>Yuv444P8</c> picture is taken plane for plane: those are already this format's own samples —
  /// full range and full resolution, which is exactly what ffmpeg calls <c>yuvj444p</c> and what its own
  /// RoQ decoder produces — so converting them to colour and back would round twice for nothing.
  /// Anything else comes through colour, since that is the one representation every pixel format here
  /// can be read as.
  /// </remarks>
  private void _Load(RawImage frame) {
    if (frame.Format == PixelFormat.Yuv444P8) {
      frame.GetPlaneData(0).CopyTo(this._source.Y);
      frame.GetPlaneData(1).CopyTo(this._source.Cb);
      frame.GetPlaneData(2).CopyTo(this._source.Cr);
      return;
    }

    RoqColorConversion.FromRgb24(frame.ToRgb24(), this._source);
  }

  // ============================================================================================
  // The chunks
  // ============================================================================================

  private void _WriteInfo(List<byte> into) {
    Span<byte> payload = stackalloc byte[_INFO_PAYLOAD_LENGTH];
    _PutUInt16(payload, this._width);
    _PutUInt16(payload[2..], this._height);
    _PutUInt16(payload[4..], _INFO_MACROBLOCK);
    _PutUInt16(payload[6..], _INFO_SUBBLOCK);

    _WriteHeader(into, RoqChunkType.INFO, 0, _INFO_PAYLOAD_LENGTH);
    foreach (var value in payload)
      into.Add(value);
  }

  private static void _WriteChunk(List<byte> into, ushort id, ushort argument, List<byte> payload) {
    _WriteHeader(into, id, argument, payload.Count);
    into.AddRange(payload);
  }

  private static void _WriteHeader(List<byte> into, ushort id, ushort argument, int length) {
    into.Add((byte)id);
    into.Add((byte)(id >> 8));
    into.Add((byte)length);
    into.Add((byte)(length >> 8));
    into.Add((byte)(length >> 16));
    into.Add((byte)(length >> 24));
    into.Add((byte)argument);
    into.Add((byte)(argument >> 8));
  }

  /// <summary>The two counts a codebook chunk's argument states, a full 256 of either spelled as
  /// nought — the only spelling the format has for it, and one its own length tells from a real
  /// nought.</summary>
  private static ushort _CodebookArgument(int cells, int quads)
    => (ushort)(((cells & 0xFF) << 8) | (quads & 0xFF));

  private static void _PutUInt16(Span<byte> into, int value) {
    into[0] = (byte)value;
    into[1] = (byte)(value >> 8);
  }
}
