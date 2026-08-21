using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.Codecs.Roq;
using FileFormat.Core;
using FileFormat.RoqVideo;

namespace FileFormat.Codecs;

/// <summary>
/// Decodes id RoQ (<c>RoQV</c>) — the FMV format Quake III and its contemporaries use — vector
/// quantisation with motion compensation over a quadtree of 8x8, 4x4 and 2x2 blocks.
/// </summary>
/// <remarks>
/// A picture is not one packet in the way a Cinepak frame is; RoQ's own chunk boundaries carry more
/// structure than that. This decoder is handed the raw <c>INFO</c>, <c>QUAD_CODEBOOK</c> and
/// <c>QUAD_VQ</c> chunks <see cref="RoqReader"/> hands out, header included, and figures out from each
/// packet's own two-byte chunk id which of them it is — the same seam Cinepak and Microsoft Video 1
/// use, where a codec reads its own framing out of the bytes it is given rather than being told by the
/// container. Only <c>QUAD_VQ</c> ever produces a picture; an <c>INFO</c> restatement or a codebook
/// update is consumed for its own effect and answers "not yet."
/// <para/>
/// <b>Two picture buffers, not one.</b> See <see cref="RoqPictureDecoder"/> for the measured argument:
/// a <c>MOT</c> block leaves in place whichever content the buffer it is being painted into last held —
/// which is two pictures back, not one, because RoQ's encoder alternates between exactly two buffers
/// and a block a picture skips is a block that buffer's *other* recent occupant never touched either.
/// The first picture has no second buffer to inherit from, so its result is copied into both slots once
/// it is painted.
/// <para/>
/// <b>Full-resolution chroma.</b> A codebook cell states one Cb and one Cr for a 2x2 area, which reads
/// as 4:2:0 — but motion compensation moves whatever a block already holds at full pixel precision,
/// chroma included, so a picture a few frames past its last codebook repaint routinely has chroma that
/// lines up with no 2x2 grid at all. <see cref="RoqFrame"/> keeps Cb and Cr at the picture's own full
/// size for exactly that reason, matching what ffmpeg's own decoder does — its native output for RoQ
/// is <c>yuvj444p</c>, not <c>yuvj420p</c>.
/// <para/>
/// <b>Measured, and measured on the right thing.</b> A picture is reconstructed as full-resolution
/// YCbCr with no subsampling left in it — see the note above — so unlike a genuinely 4:2:0 codec there
/// is no chroma-siting convention for an RGB comparison to disagree about, and a comparison there would
/// be a direct one in principle. In practice ffmpeg's own <c>rgb24</c> output is not perfectly faithful
/// to its own decoded planes — on two of three files measured, a few dozen pixels across the whole file
/// differ from this decoder's RGB by one level, and reproducing this decoder's own conversion formula
/// against ffmpeg's <c>yuvj444p</c> planes directly, with no decoder of ours involved, reproduces the
/// identical handful of pixels at the identical positions, which is what settles that as ffmpeg's
/// <c>swscale</c> and not a decoding difference. So the decode itself is measured on <c>yuvj444p</c>,
/// where the answer is unambiguous either way. Three files — 512x256 to 512x512, 210 to 802 pictures,
/// 1 338 in all, one of them the sample whose own accompanying note names motion compensation with a
/// nonzero mean vector as "the last problem in the native roq decoder" for chrominance addressing —
/// were decoded here and by ffmpeg and compared sample for sample against ffmpeg's own <c>yuvj444p</c>
/// output: every plane of every picture in all three files is identical, ffmpeg's decode included on
/// the file the sample's own author flags as exercising the bug.
/// <para/>
/// <b>What is not implemented refuses and says so.</b> A <c>RoQ_JPEG</c> chunk — the 11th Hour and
/// Clandestiny superset of the format, where a keyframe may be a plain JFIF file instead of a
/// quadtree-coded picture — is refused by name rather than guessed at; no sample this was measured
/// against carries one. A picture size that is not a whole number of 16-pixel macroblocks, a size that
/// changes part way through a stream, a codebook entry named before any codebook chunk has stated one,
/// and a motion vector reaching outside the picture all refuse and name the field that failed, rather
/// than being clamped or wrapped against nothing that was ever verified.
/// </remarks>
public sealed class RoqVideoDecoder : IVideoCodecDecoder<RoqVideoDecoder> {

  private static readonly CodecTag _Tag = CodecTag.FromCharacters("RoQV");

  private const int _CHUNK_HEADER_LENGTH = 8;
  private const int _INFO_PAYLOAD_LENGTH = 8;
  private const int _MACROBLOCK = 16;

  private readonly RoqCodebook _codebook = new();

  private int _width;
  private int _height;
  private RoqFrame? _bufferA;
  private RoqFrame? _bufferB;
  private bool _nextTargetIsA = true;
  private bool _hasDecodedFirstPicture;

  public static string CodecName => "id RoQ";

  public static bool Accepts(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);

    return stream.Kind == MediaStreamKind.Video && stream.Codec.EqualsIgnoringCase(_Tag);
  }

  public static RoqVideoDecoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);

    return new();
  }

  public bool TryDecode(CodedPacket packet, out RawImage frame) {
    var data = packet.Data.Span;
    if (data.Length < _CHUNK_HEADER_LENGTH)
      throw new InvalidDataException($"A RoQ packet is {data.Length} bytes, short of a chunk header's own eight.");

    var id = BinaryPrimitives.ReadUInt16LittleEndian(data);
    var size = BinaryPrimitives.ReadUInt32LittleEndian(data[2..]);
    var argument = BinaryPrimitives.ReadUInt16LittleEndian(data[6..]);
    var payload = data.Slice(_CHUNK_HEADER_LENGTH, (int)size);

    switch (id) {
      case RoqChunkType.INFO:
        this._ReadInfo(payload);
        frame = null!;
        return false;

      case RoqChunkType.QUAD_CODEBOOK:
        this._codebook.Replace(payload, argument);
        frame = null!;
        return false;

      case RoqChunkType.QUAD_VQ:
        frame = this._DecodePicture(payload, argument);
        return true;

      case RoqChunkType.JPEG:
        throw new NotSupportedException(
          "A RoQ_JPEG chunk carries a JFIF picture in place of a quadtree-coded one — the 11th Hour and "
          + "Clandestiny superset of the format. Not implemented.");

      default:
        throw new NotSupportedException($"A RoQ video packet is chunk type 0x{id:X4}, which is not one this decoder reads.");
    }
  }

  private void _ReadInfo(ReadOnlySpan<byte> payload) {
    if (payload.Length < _INFO_PAYLOAD_LENGTH)
      throw new InvalidDataException($"A RoQ_INFO chunk is {payload.Length} bytes, short of the eight bytes the chunk holds.");

    var width = BinaryPrimitives.ReadUInt16LittleEndian(payload);
    var height = BinaryPrimitives.ReadUInt16LittleEndian(payload[2..]);

    if (this._width != 0 && (width != this._width || height != this._height))
      throw new NotSupportedException(
        $"This RoQ stream states a picture of {this._width}x{this._height} and then, part way through, "
        + $"{width}x{height}. Decoding a stream whose picture size changes is not implemented.");

    if (this._width != 0)
      return;

    if (width == 0 || height == 0)
      throw new InvalidDataException($"RoQ_INFO states a picture of {width}x{height}, which has no pixels.");

    if (width % _MACROBLOCK != 0 || height % _MACROBLOCK != 0)
      throw new NotSupportedException(
        $"RoQ_INFO states a picture of {width}x{height}, which is not a whole number of {_MACROBLOCK}-pixel "
        + "macroblocks. RoQ codes nothing but whole macroblocks and states nowhere what a partial one covers.");

    this._width = width;
    this._height = height;
    this._bufferA = new(width, height);
    this._bufferB = new(width, height);
  }

  private RawImage _DecodePicture(ReadOnlySpan<byte> payload, ushort argument) {
    if (this._bufferA == null)
      throw new InvalidDataException("A RoQ_QUAD_VQ chunk arrived before any RoQ_INFO chunk stated a picture size.");

    var meanX = (sbyte)(argument >> 8);
    var meanY = (sbyte)argument;

    var target = this._nextTargetIsA ? this._bufferA : this._bufferB!;
    var reference = this._nextTargetIsA ? this._bufferB! : this._bufferA;

    RoqPictureDecoder.Decode(payload, this._codebook, meanX, meanY, reference, target);

    if (!this._hasDecodedFirstPicture) {
      // The very first picture has no second buffer to have been building into two pictures ago, so
      // its result becomes both buffers' content — see RoqPictureDecoder's remarks on MOT.
      reference.CopyFrom(target);
      this._hasDecodedFirstPicture = true;
    }

    this._nextTargetIsA = !this._nextTargetIsA;

    return new() {
      Width = this._width,
      Height = this._height,
      Format = PixelFormat.Rgb24,
      PixelData = RoqColorConversion.ToRgb24(target),
    };
  }
}
