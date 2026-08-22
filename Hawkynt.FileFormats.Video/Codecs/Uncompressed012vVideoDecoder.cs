using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>
/// Decodes 012v: 10-bit 4:2:2 YUV with nothing compressed at all, three samples packed into each of
/// four 32-bit words, which is v210's own group layout under a different rule for how long a row is.
/// </summary>
/// <remarks>
/// The name is <c>v210</c> written backwards and the resemblance is not a coincidence: the sixteen-byte
/// group is the same one, sample for sample and bit for bit —
/// <code>
/// word 0: U(0,1) bits 0-9,  Y(0) bits 10-19, V(0,1) bits 20-29
/// word 1: Y(1)   bits 0-9,  U(2,3) bits 10-19, Y(2) bits 20-29
/// word 2: V(2,3) bits 0-9,  Y(3) bits 10-19,  U(4,5) bits 20-29
/// word 3: Y(4)   bits 0-9,  V(4,5) bits 10-19, Y(5) bits 20-29
/// </code>
/// six luma samples and three chroma pairs to a group, little-endian words, ten bits a sample. What
/// differs is the row: v210 pads every row out to a whole 128 bytes, and 012v does not — a row here is
/// as long as the packet's own length divided by the picture's height says it is.
/// <para/>
/// <b>The top two bits of every ten-bit field are not padding to be trusted.</b> They are masked off
/// rather than assumed zero, because the one real sample does not leave them zero: seven of its 50,880
/// words carry something in bits 30 and 31, and the reference decoder ignores them. Reading a field as
/// sixteen bits and hoping would put those seven words' samples wildly out.
/// <para/>
/// <b>A group past the picture's own width is read and thrown away.</b> A 316-pixel row is 53 whole
/// groups, which is 318 luma samples and 159 chroma columns; the two luma samples and the one chroma
/// column past the width are decoded and discarded rather than assumed absent, the same as v210's.
/// <para/>
/// <b>Where the "transparency is not implemented" message comes from, and why it is not this codec's.</b>
/// The reference decoder serves two four-character codes from one implementation, <c>012v</c> and
/// <c>a12v</c>, and logs that message for the second — the one that carries an alpha channel it drops.
/// Patching nothing but the four tag bytes of the real sample from <c>012v</c> to <c>a12v</c> makes the
/// message appear on byte-identical picture data, and the unpatched file decodes with no warning at
/// all. So the incompleteness belongs to the sibling code and not to this one, and the oracle is sound
/// here in a way it would not be for <c>a12v</c>. Only <c>012v</c> is claimed; <c>a12v</c> is not
/// accepted, having neither a sample nor a decoder anywhere that states it is complete.
/// <para/>
/// <b>Verified on the planes, at the coded depth, and exactly.</b> This is a lossless packing of the
/// ten-bit samples themselves, so <see cref="DecodePlanes"/> was compared against ffmpeg's own
/// <c>-pix_fmt yuv422p10le</c> output of the same packet — luma at the full width, chroma at half of
/// it, both at the full height. The one sample that exists, <c>fate-suite.ffmpeg.org/012v/sample.avi</c>
/// at 316x240 in a single 203,520-byte packet, comes back **identical on all 303,360 bytes of its three
/// planes**. The comparison is at ten bits on purpose: taking it through eight-bit RGB would stack a
/// depth reduction, a chroma siting and a colour conversion on top of a decode that is exact, and
/// report a difference none of them is this decoder's.
/// <para/>
/// <b>What refuses.</b> A picture with no pixels; a packet whose length is not a whole number of rows;
/// and a row shorter than the whole groups its width needs. That last one is a real limit rather than a
/// formality: the format permits a final group cut short — a trailing pair of pixels costing five bytes
/// and a trailing single one two — and no file measured here uses it, the one sample's rows being 848
/// bytes where the cut-short rule would make them 842. Reading a truncated final group is refused rather
/// than guessed at, because the guess would be a picture with its last columns wrong and nothing to say
/// so.
/// </remarks>
public sealed class Uncompressed012vVideoDecoder : IVideoCodecDecoder<Uncompressed012vVideoDecoder> {

  private static readonly CodecTag _Tag = CodecTag.FromCharacters("012v");

  private readonly int _width;
  private readonly int _height;
  private readonly int _streamIndex;
  private readonly int _chromaWidth;
  private readonly int _groupsPerLine;

  private Uncompressed012vVideoDecoder(int width, int height, int streamIndex) {
    this._width = width;
    this._height = height;
    this._streamIndex = streamIndex;
    this._chromaWidth = (width + 1) / 2;
    this._groupsPerLine = (width + 5) / 6;
  }

  public static string CodecName => "Uncompressed 4:2:2 10-bit (012v)";

  public static bool Accepts(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);

    return stream.Kind == MediaStreamKind.Video && stream.Codec.EqualsIgnoringCase(_Tag);
  }

  public static Uncompressed012vVideoDecoder Create(MediaStreamInfo stream) {
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
  /// <remarks>
  /// The row length is the packet's own, divided by the height. Nothing in the stream description
  /// states it and there is no padding rule to compute it from, which is the one place this format
  /// differs from v210 at all.
  /// </remarks>
  internal (ushort[] Luma, ushort[] Cb, ushort[] Cr) DecodePlanes(ReadOnlySpan<byte> data) {
    var stride = this._StrideOf(data.Length);

    var luma = new ushort[this._width * this._height];
    var cb = new ushort[this._chromaWidth * this._height];
    var cr = new ushort[this._chromaWidth * this._height];

    Span<ushort> y6 = stackalloc ushort[6];
    Span<ushort> u3 = stackalloc ushort[3];
    Span<ushort> v3 = stackalloc ushort[3];

    for (var row = 0; row < this._height; ++row) {
      var line = data.Slice(row * stride, stride);
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
  /// Works out how long a row is from the packet's own length, and refuses anything that is not a
  /// whole number of rows of whole groups.
  /// </summary>
  private int _StrideOf(int length) {
    var whole = this._groupsPerLine * 16;

    if (length % this._height != 0)
      throw new InvalidDataException(
        $"Video stream {this._streamIndex} carries a 012v packet of {length} byte(s), which is not a whole number of "
        + $"rows for a picture {this._height} row(s) high. This format states its row length nowhere, so the packet's "
        + "own length divided by the height is the only thing there is to read it from.");

    var stride = length / this._height;
    if (stride < whole)
      throw new NotSupportedException(
        $"Video stream {this._streamIndex} carries a 012v packet whose rows are {stride} byte(s), where the "
        + $"{this._width} pixels of a row need {whole} as whole sixteen-byte groups. The format allows a final group "
        + "cut short, and no file measured here uses it, so it is refused rather than read under a packing nothing "
        + "confirms.");

    return stride;
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
        // Ten bits reduced to eight by the plain halving studio-swing conventions use, before the
        // matrix — the same reduction this package's v210 decoder makes for the same samples.
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
