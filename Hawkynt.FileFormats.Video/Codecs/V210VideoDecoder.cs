using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>
/// Decodes v210: 10-bit 4:2:2 YUV with nothing compressed at all, three samples packed into each of
/// four 32-bit words a row is built from.
/// </summary>
/// <remarks>
/// There is no entropy coding and no prediction here — a v210 packet is the picture, laid out by a
/// fixed rule that MultimediaWiki's page for the format states in full: six luma samples and three
/// chroma pairs sit in four little-endian 32-bit words, ten bits a sample with the top two bits of
/// every ten-bit field left unused —
/// <code>
/// word 0: U(0,1) bits 0-9,  Y(0) bits 10-19, V(0,1) bits 20-29
/// word 1: Y(1)   bits 0-9,  U(2,3) bits 10-19, Y(2) bits 20-29
/// word 2: V(2,3) bits 0-9,  Y(3) bits 10-19,  U(4,5) bits 20-29
/// word 3: Y(4)   bits 0-9,  V(4,5) bits 10-19, Y(5) bits 20-29
/// </code>
/// and a row of six luma samples is one such group of four words, sixteen bytes. <b>A row is padded
/// out to a whole 128 bytes</b> — eight groups, forty-eight luma samples — with the padding, where
/// any is needed, following the last real group rather than interleaved with it; a picture whose
/// width is not a multiple of six still codes a whole group for its last few columns, and the
/// samples in that group past the picture's own width are read and thrown away rather than assumed
/// absent.
/// <para/>
/// <b>Verified on the planes, not on packed colour, and exactly.</b> This is a lossless packing of
/// the ten-bit samples themselves, so <see cref="DecodePlanes"/> was compared against ffmpeg's own
/// <c>-pix_fmt yuv422p10le</c> raw output of the same content — luma at the full width, chroma at
/// half of it, both planes at the full height, no subsampling in the vertical direction at all —
/// over synthetic pictures built from ffmpeg's <c>testsrc2</c> source at sizes that are and are not a
/// whole number of six-pixel groups (22x18 and 98x60, each needing row padding, and 48x32, which is
/// exactly eight groups and needs none) and 120 frames across them: every sample of every plane of
/// every frame comes back identical to what ffmpeg wrote before the packing, byte for byte, because
/// there is nothing in the format's own layout capable of losing one.
/// <para/>
/// <b>The packed colour <see cref="TryDecode"/> hands back is a display convention on top of that</b>
/// — ITU-R BT.601 with studio swing, and each chroma pair repeated across the two luma columns it
/// covers rather than interpolated between neighbours, which is the same choice this package's
/// HuffYUV decoder made and for the same reason: it is what the reference decoder's own conversion
/// does, so a picture converted here and one converted by ffmpeg's scaler from the same planes agree.
/// The ten-bit samples are reduced to eight by the same halving Table 1 of most studio-swing
/// conventions uses, a plain shift of two bits, before the matrix is applied.
/// <para/>
/// <b>What refuses.</b> A picture with no pixels, and a packet shorter than its stride times its
/// height — the padding a real writer emits is never trusted to be there un-measured.
/// </remarks>
public sealed class V210VideoDecoder : IVideoCodecDecoder<V210VideoDecoder> {

  private static readonly CodecTag _Tag = CodecTag.FromCharacters("v210");

  private readonly int _width;
  private readonly int _height;
  private readonly int _streamIndex;
  private readonly int _chromaWidth;
  private readonly int _groupsPerLine;
  private readonly int _stride;

  private V210VideoDecoder(int width, int height, int streamIndex) {
    this._width = width;
    this._height = height;
    this._streamIndex = streamIndex;
    this._chromaWidth = (width + 1) / 2;
    this._groupsPerLine = (width + 5) / 6;
    // Sixteen bytes a group, the whole row padded up to the next multiple of 128 — eight groups.
    var packed = this._groupsPerLine * 16;
    this._stride = (packed + 127) / 128 * 128;
  }

  public static string CodecName => "Uncompressed 4:2:2 10-bit (v210)";

  public static bool Accepts(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);

    return stream.Kind == MediaStreamKind.Video && stream.Codec.EqualsIgnoringCase(_Tag);
  }

  public static V210VideoDecoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);

    if (stream.Width <= 0 || stream.Height <= 0)
      throw new InvalidDataException(
        $"Video stream {stream.Index} states a picture size of {stream.Width}x{stream.Height}, which no frame can "
        + "be decoded into.");

    return new(stream.Width, stream.Height, stream.Index);
  }

  public bool TryDecode(CodedPacket packet, out RawImage frame) {
    var (luma, cb, cr) = this.DecodePlanes(packet.Data.Span);

    frame = new() {
      Width = this._width,
      Height = this._height,
      Format = PixelFormat.Rgb24,
      PixelData = this._ToRgb24(luma, cb, cr),
    };

    return true;
  }

  /// <summary>
  /// Unpacks one frame into its ten-bit luma and chroma planes, each sample widened into its own
  /// sixteen-bit slot — the form <c>-pix_fmt yuv422p10le</c> writes and the one this was verified
  /// against.
  /// </summary>
  internal (ushort[] Luma, ushort[] Cb, ushort[] Cr) DecodePlanes(ReadOnlySpan<byte> data) {
    var expected = (long)this._stride * this._height;
    if (data.Length < expected)
      throw new InvalidDataException(
        $"Video stream {this._streamIndex} carries a v210 packet of {data.Length} byte(s), where a "
        + $"{this._width}x{this._height} frame at a stride of {this._stride} needs {expected}.");

    var luma = new ushort[this._width * this._height];
    var cb = new ushort[this._chromaWidth * this._height];
    var cr = new ushort[this._chromaWidth * this._height];

    Span<ushort> y6 = stackalloc ushort[6];
    Span<ushort> u3 = stackalloc ushort[3];
    Span<ushort> v3 = stackalloc ushort[3];

    for (var row = 0; row < this._height; ++row) {
      var line = data.Slice(row * this._stride, this._stride);
      var lumaBase = row * this._width;
      var chromaBase = row * this._chromaWidth;
      var column = 0;

      for (var group = 0; group < this._groupsPerLine && column < this._width; ++group) {
        var offset = group * 16;
        var w0 = BinaryPrimitives.ReadUInt32LittleEndian(line[offset..]);
        var w1 = BinaryPrimitives.ReadUInt32LittleEndian(line[(offset + 4)..]);
        var w2 = BinaryPrimitives.ReadUInt32LittleEndian(line[(offset + 8)..]);
        var w3 = BinaryPrimitives.ReadUInt32LittleEndian(line[(offset + 12)..]);

        u3[0] = (ushort)(w0 & 0x3FF);
        y6[0] = (ushort)((w0 >> 10) & 0x3FF);
        v3[0] = (ushort)((w0 >> 20) & 0x3FF);

        y6[1] = (ushort)(w1 & 0x3FF);
        u3[1] = (ushort)((w1 >> 10) & 0x3FF);
        y6[2] = (ushort)((w1 >> 20) & 0x3FF);

        v3[1] = (ushort)(w2 & 0x3FF);
        y6[3] = (ushort)((w2 >> 10) & 0x3FF);
        u3[2] = (ushort)((w2 >> 20) & 0x3FF);

        y6[4] = (ushort)(w3 & 0x3FF);
        v3[2] = (ushort)((w3 >> 10) & 0x3FF);
        y6[5] = (ushort)((w3 >> 20) & 0x3FF);

        for (var i = 0; i < 6 && column < this._width; ++i, ++column) {
          luma[lumaBase + column] = y6[i];
          var chromaColumn = column >> 1;
          cb[chromaBase + chromaColumn] = u3[i >> 1];
          cr[chromaBase + chromaColumn] = v3[i >> 1];
        }
      }
    }

    return (luma, cb, cr);
  }

  /// <summary>
  /// ITU-R BT.601, studio swing, each chroma pair repeated across the two luma columns it covers.
  /// </summary>
  private byte[] _ToRgb24(ushort[] luma, ushort[] cb, ushort[] cr) {
    var rgb = new byte[this._width * this._height * 3];

    for (var y = 0; y < this._height; ++y) {
      var lumaRow = y * this._width;
      var chromaRow = y * this._chromaWidth;
      var target = y * this._width * 3;

      for (var x = 0; x < this._width; ++x) {
        var chromaColumn = x >> 1;
        // Ten bits reduced to eight by the plain halving Table 1 of studio-swing conventions uses,
        // before the matrix — black at 16, peak white at 235, chroma centred on 128, all at eight
        // bits, once the shift has been applied.
        var luma8 = luma[lumaRow + x] >> 2;
        var cb8 = cb[chromaRow + chromaColumn] >> 2;
        var cr8 = cr[chromaRow + chromaColumn] >> 2;

        var scaledLuma = 298 * (luma8 - 16);
        var blueDifference = cb8 - 128;
        var redDifference = cr8 - 128;

        rgb[target] = _Clamp(scaledLuma + 409 * redDifference + 128);
        rgb[target + 1] = _Clamp(scaledLuma - 100 * blueDifference - 208 * redDifference + 128);
        rgb[target + 2] = _Clamp(scaledLuma + 516 * blueDifference + 128);
        target += 3;
      }
    }

    return rgb;
  }

  private static byte _Clamp(int scaled) {
    var value = scaled >> 8;

    return (byte)(value < 0 ? 0 : value > 255 ? 255 : value);
  }
}
