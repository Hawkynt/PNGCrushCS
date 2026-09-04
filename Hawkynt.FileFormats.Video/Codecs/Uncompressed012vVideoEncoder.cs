using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>
/// Encodes 012v: 10-bit 4:2:2 YUV with nothing compressed at all, v210's own sixteen-byte group with
/// no row padding at all.
/// </summary>
/// <remarks>
/// The mirror of <see cref="Uncompressed012vVideoDecoder"/>, and the group is
/// <see cref="V210VideoEncoder"/>'s own — six luma samples and three chroma pairs in four
/// little-endian 32-bit words, ten bits a sample, the top two bits of every word left zero:
/// <code>
/// word 0: U(0,1) bits 0-9,  Y(0) bits 10-19, V(0,1) bits 20-29
/// word 1: Y(1)   bits 0-9,  U(2,3) bits 10-19, Y(2) bits 20-29
/// word 2: V(2,3) bits 0-9,  Y(3) bits 10-19,  U(4,5) bits 20-29
/// word 3: Y(4)   bits 0-9,  V(4,5) bits 10-19, Y(5) bits 20-29
/// </code>
/// What differs is the row, and it is the only thing that does: v210 pads every row out to a whole
/// 128 bytes and this pads nothing, so a row is exactly as many whole groups as the width needs.
/// That is what makes the row length recoverable from the packet at all — the decoder has no padding
/// rule to compute it from and reads it as the packet's own length over the picture's height, so a
/// packet that padded would decode at the wrong stride.
/// <para/>
/// <b>A last group past the width is written whole, with zeros in it.</b> The format permits a final
/// group cut short and no file measured anywhere uses one; the decoder refuses a short one rather
/// than guess at its packing, so writing one would produce a packet this package could not read back.
/// A picture of odd width codes a whole last chroma pair for its last column, which is where
/// <see cref="PixelFormat.Yuv422P10"/> keeps one as well.
/// <para/>
/// <b>Lossless on the planes.</b> The ten-bit samples go into the packet untouched, so a
/// <see cref="PixelFormat.Yuv422P10"/> picture's planes come back identical. Any other format is
/// first converted to that one under the same ITU-R BT.601 studio-swing convention the decoder
/// displays with.
/// <para/>
/// <b>Verified against ffmpeg's own decode, exactly.</b> The reference has no 012v encoder to compare
/// bytes against — the codec is decode-only there — so the oracle is its decoder: packets written
/// here were muxed into an AVI and read back through ffmpeg 9 as <c>yuv422p10le</c> planes, over
/// pseudo-random pictures at 12x8, 316x4 (the one real sample's width), 7x5 and 6x3, every sample of
/// every plane identical to the one that went in.
/// <para/>
/// <b>What refuses.</b> A stream that is not video or has no pixels, a frame whose geometry differs
/// from the stream's, and a sample above 1023 — a <see cref="PixelFormat.Yuv422P10"/> sample is
/// right-justified in its sixteen bits, and a value the ten bits cannot hold would be written wrong
/// rather than clipped.
/// </remarks>
public sealed class Uncompressed012vVideoEncoder : IVideoCodecEncoder<Uncompressed012vVideoEncoder> {

  private static readonly CodecTag _Tag = CodecTag.FromCharacters("012v");

  private readonly MediaStreamInfo _stream;
  private readonly int _width;
  private readonly int _height;
  private readonly int _chromaWidth;
  private readonly int _groupsPerLine;
  private readonly int _stride;

  private Uncompressed012vVideoEncoder(MediaStreamInfo stream) {
    this._width = stream.Width;
    this._height = stream.Height;
    this._chromaWidth = (stream.Width + 1) / 2;
    this._groupsPerLine = (stream.Width + 5) / 6;
    // Sixteen bytes a group and nothing beyond them: this format's whole difference from v210.
    this._stride = this._groupsPerLine * 16;
    this._stream = new() {
      Index = stream.Index,
      Kind = MediaStreamKind.Video,
      Codec = _Tag,
      Handler = _Tag,
      TimeBase = stream.TimeBase,
      FrameRate = stream.FrameRate,
      DeclaredFrameCount = stream.DeclaredFrameCount,
      Width = stream.Width,
      Height = stream.Height,
      BitsPerPixel = 20,
      Language = stream.Language,
      Name = stream.Name,
    };
  }

  public static string CodecName => "Uncompressed 4:2:2 10-bit (012v)";

  public static CodecTag Codec => _Tag;

  public static Uncompressed012vVideoEncoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);

    if (stream.Kind != MediaStreamKind.Video)
      throw new NotSupportedException("012v can only encode a video stream.");

    if (stream.Width <= 0 || stream.Height <= 0)
      throw new InvalidDataException(
        $"Video stream {stream.Index} states a picture size of {stream.Width}x{stream.Height}, which no frame can "
        + "be coded from.");

    return new(stream);
  }

  public bool TryEncode(RawImage frame, long? presentationTimestamp, out CodedPacket packet) {
    ArgumentNullException.ThrowIfNull(frame);
    if (frame.Width != this._width || frame.Height != this._height)
      throw new InvalidDataException(
        $"012v geometry is fixed at {this._width}x{this._height}; received {frame.Width}x{frame.Height}.");
    if (!frame.HasEnoughPixelData)
      throw new InvalidDataException("The source RawImage does not contain enough pixel data for its declared format and dimensions.");

    var source = frame.Format == PixelFormat.Yuv422P10
      ? frame
      : FastRawImageConverter.Convert(frame, PixelFormat.Yuv422P10, RawImageColorInfo.Bt601Limited);
    if (!source.HasEnoughPixelData)
      throw new InvalidDataException($"Conversion to {PixelFormat.Yuv422P10} produced too few bytes for {this._width}x{this._height}.");

    var luma = _Samples(source.GetPlaneData(0), this._width * this._height);
    var cb = _Samples(source.GetPlaneData(1), this._chromaWidth * this._height);
    var cr = _Samples(source.GetPlaneData(2), this._chromaWidth * this._height);

    packet = new(
      this._stream.Index,
      this.EncodePlanes(luma, cb, cr),
      PresentationTimestamp: presentationTimestamp,
      DecodeTimestamp: presentationTimestamp,
      Duration: 1,
      IsKeyFrame: true);
    return true;
  }

  public MediaStreamInfo DescribeStream() => this._stream;

  /// <summary>
  /// Packs one frame from its ten-bit luma and chroma planes, each sample in its own sixteen-bit slot
  /// — the form <c>-pix_fmt yuv422p10le</c> writes, luma at the full width, chroma at half of it,
  /// every plane at the full height.
  /// </summary>
  internal byte[] EncodePlanes(ReadOnlySpan<ushort> luma, ReadOnlySpan<ushort> cb, ReadOnlySpan<ushort> cr) {
    var lumaSamples = this._width * this._height;
    var chromaSamples = this._chromaWidth * this._height;
    if (luma.Length < lumaSamples || cb.Length < chromaSamples || cr.Length < chromaSamples)
      throw new InvalidDataException(
        $"A {this._width}x{this._height} 012v frame needs {lumaSamples} luma and {chromaSamples} samples a chroma plane; "
        + $"received {luma.Length}, {cb.Length} and {cr.Length}.");

    var data = new byte[checked(this._stride * this._height)];
    Span<uint> y6 = stackalloc uint[6];
    Span<uint> u3 = stackalloc uint[3];
    Span<uint> v3 = stackalloc uint[3];

    for (var row = 0; row < this._height; ++row) {
      var line = data.AsSpan(row * this._stride, this._stride);
      var lumaBase = row * this._width;
      var chromaBase = row * this._chromaWidth;
      var column = 0;

      for (var group = 0; group < this._groupsPerLine; ++group) {
        // Samples past the picture's own width are written as zero; the decoder throws them away.
        y6.Clear();
        u3.Clear();
        v3.Clear();
        for (var i = 0; i < 6 && column < this._width; ++i, ++column) {
          y6[i] = _Ten(luma[lumaBase + column], "luma");
          var chromaColumn = column >> 1;
          u3[i >> 1] = _Ten(cb[chromaBase + chromaColumn], "blue difference");
          v3[i >> 1] = _Ten(cr[chromaBase + chromaColumn], "red difference");
        }

        var offset = group * 16;
        BinaryPrimitives.WriteUInt32LittleEndian(line[offset..], u3[0] | (y6[0] << 10) | (v3[0] << 20));
        BinaryPrimitives.WriteUInt32LittleEndian(line[(offset + 4)..], y6[1] | (u3[1] << 10) | (y6[2] << 20));
        BinaryPrimitives.WriteUInt32LittleEndian(line[(offset + 8)..], v3[1] | (y6[3] << 10) | (u3[2] << 20));
        BinaryPrimitives.WriteUInt32LittleEndian(line[(offset + 12)..], y6[4] | (v3[2] << 10) | (y6[5] << 20));
      }
    }

    return data;
  }

  /// <summary>Widens one plane of little-endian sixteen-bit slots into the samples they hold.</summary>
  private static ushort[] _Samples(ReadOnlySpan<byte> plane, int count) {
    var samples = new ushort[count];
    for (var i = 0; i < count; ++i)
      samples[i] = BinaryPrimitives.ReadUInt16LittleEndian(plane[(i * 2)..]);

    return samples;
  }

  private static uint _Ten(ushort sample, string component)
    => sample <= 0x3FF
      ? sample
      : throw new InvalidDataException($"A 012v {component} sample is ten bits wide; {sample} does not fit.");
}
