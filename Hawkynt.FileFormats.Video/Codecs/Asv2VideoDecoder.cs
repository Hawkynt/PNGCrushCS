using System;
using System.Collections.Generic;
using System.IO;
using FileFormat.Bmp;
using FileFormat.Codecs.Asv2;
using FileFormat.Codecs.H263;
using FileFormat.Codecs.Mpeg;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>
/// Decodes ASUS V2, ASUSTeK's successor to ASV1 for the same TV tuner cards, coded from the same
/// document ASV1 is: Michael Niedermayer's "ASUS V1/V2 Codecs" (asv1.txt, 2003-2016, GNU FDL/GPL).
/// </summary>
/// <remarks>
/// ASV2 keeps ASV1's macroblock shape, its per-file quantisation parameter and its dequantisation
/// against ISO/IEC 11172-2's own intra matrix, and changes three things: the packet's bit order is
/// reversed within each byte rather than swapped by four-byte word
/// (<see cref="Asv2Bitstream.ReverseBits"/>); a block states how many coefficient groups follow up
/// front rather than ending on an End Of Block code, so it can use every one of the sixteen groups
/// clause 3.3's diagram names rather than only the first ten ASV1's End-Of-Block-terminated coding can
/// reach; and the dequantisation scale is a hundred and twenty-eight rather than sixty-four, which
/// clause 3.6 states outright.
/// <para/>
/// Like ASV1 this reuses ITU-T H.263's picture buffer, inverse transform and studio-range colour
/// conversion rather than writing new copies of them, because a macroblock is the same 4:2:0 shape in
/// the same quadrant order. Every picture is coded independently, so a packet always decodes to exactly
/// one picture and nothing is ever held back.
/// <para/>
/// <b>What the document leaves as an ellipsis, and how this closes it.</b> Clause 5.2.3's level table
/// prints magnitudes one to seven and the boundary magnitude thirty-one in full and leaves magnitudes
/// eight to thirty unstated. Every printed value fits one formula — a nested code of <c>k</c> zero bits,
/// a one, <c>k</c> further bits and a sign, with the <c>k</c> bits read as a magnitude offset least
/// significant bit first — and applying that formula to the unstated range was checked against real
/// encoded pictures rather than shipped on the strength of the pattern alone; see
/// <see cref="Asv2VlcTables.Level"/> and this codec's section of <c>README.md</c> for the measurement.
/// <para/>
/// <b>The same two coordinate diagrams ASV1 needed measurement for</b> — the sixteen coefficient groups
/// across a block and the four positions within one group, both drawn as a page reads and both needing
/// their row and column swapped before a real picture reconstructs — are the same diagrams here, so the
/// same swap applies; see <see cref="Asv2VlcTables.ScanPosition"/>. What is ASV2's own, settled the same
/// way, is that a coefficient group's pattern bit for one of its four positions is read most significant
/// bit first, where ASV1 reads the same shape of pattern least significant bit first.
/// <para/>
/// There is no <c>catch</c> anywhere that hands back a blank, a copied or a zero-filled picture.
/// </remarks>
public sealed class Asv2VideoDecoder : IVideoCodecDecoder<Asv2VideoDecoder> {

  private static readonly CodecTag _Tag = CodecTag.FromCharacters("ASV2");

  /// <summary>How many bytes of codec-private data ASV2's own global header needs behind the BITMAPINFOHEADER.</summary>
  private const int _GlobalHeaderSize = 8;

  private readonly int _width;
  private readonly int _height;
  private readonly int _macroblockWidth;
  private readonly int _macroblockHeight;
  private readonly int[] _dequantFactors;

  private Asv2VideoDecoder(int width, int height, int quantiser) {
    this._width = width;
    this._height = height;
    this._macroblockWidth = (width + 15) / 16;
    this._macroblockHeight = (height + 15) / 16;

    // Clause 3.6: floor(D * q[i] / QP), D = 128 for ASV2, built once from the file's one quantiser.
    var factors = new int[64];
    for (var i = 0; i < 64; ++i)
      factors[i] = 128 * MpegQuantisation.DefaultIntraMatrix[i] / quantiser;
    this._dequantFactors = factors;
  }

  public static string CodecName => "ASUS V2";

  public static bool Accepts(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);

    return stream.Kind == MediaStreamKind.Video && stream.Codec.EqualsIgnoringCase(_Tag);
  }

  public static Asv2VideoDecoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);

    if (stream.Width <= 0 || stream.Height <= 0)
      throw new InvalidDataException(
        $"Video stream {stream.Index} states a picture size of {stream.Width}x{stream.Height}, which no frame can "
        + "be decoded into.");

    var format = stream.CodecPrivateData.Span;
    if (format.Length < BitmapInfoHeader.StructSize + _GlobalHeaderSize)
      throw new InvalidDataException(
        $"Video stream {stream.Index} carries {format.Length} byte(s) behind its BITMAPINFOHEADER, where ASV2's own "
        + $"eight-byte global header (asv1.txt 4.2) needs at least {BitmapInfoHeader.StructSize + _GlobalHeaderSize}.");

    var quantiser = format[BitmapInfoHeader.StructSize];
    if (quantiser == 0)
      throw new InvalidDataException(
        $"Video stream {stream.Index} states an ASV2 quantisation parameter of zero, which the dequantisation of "
        + "asv1.txt 3.6 divides by.");

    return new(stream.Width, stream.Height, quantiser);
  }

  public bool TryDecode(CodedPacket packet, out RawImage frame) {
    var target = this._DecodePlanes(packet);

    frame = new() {
      Width = this._width,
      Height = this._height,
      Format = PixelFormat.Rgb24,
      PixelData = H263ColorConversion.ToRgb24(target, this._width, this._height),
    };
    return true;
  }

  /// <summary>
  /// Decodes one packet to its reconstructed 4:2:0 planes, ahead of the colour conversion every caller
  /// of <see cref="TryDecode"/> gets instead. Internal so that a comparison against another decoder's
  /// planes never has to go through a colour conversion at all.
  /// </summary>
  internal H263Frame _DecodePlanes(CodedPacket packet) {
    var reversed = Asv2Bitstream.ReverseBits(packet.Data.Span);
    var reader = new H263BitReader(reversed);
    var target = new H263Frame(this._macroblockWidth, this._macroblockHeight);
    Span<int> block = stackalloc int[64];

    foreach (var (mbX, mbY) in this._MacroblockOrder())
      for (var index = 0; index < 6; ++index) {
        Asv2BlockDecoder.Read(ref reader, block, this._dequantFactors);
        this._Store(target, mbX, mbY, index, block);
      }

    return target;
  }

  /// <summary>Nothing is ever held back: ASV2 has no prediction between pictures to reorder around.</summary>
  public IEnumerable<RawImage> Flush() => [];

  private void _Store(H263Frame target, int mbX, int mbY, int index, ReadOnlySpan<int> samples) {
    var (plane, width, _) = target.PlaneOf(index);
    var (left, top) = index < 4
      ? (mbX * 16 + (index & 1) * 8, mbY * 16 + (index >> 1) * 8)
      : (mbX * 8, mbY * 8);

    for (var y = 0; y < 8; ++y) {
      var row = (top + y) * width + left;
      for (var x = 0; x < 8; ++x) {
        var value = samples[y * 8 + x];
        plane[row + x] = (byte)(value < 0 ? 0 : value > 255 ? 255 : value);
      }
    }
  }

  /// <summary>
  /// Clause 3.1: the whole macroblocks in raster order, then the partial-width column top to bottom,
  /// then the partial-height row — corner included — left to right.
  /// </summary>
  private IEnumerable<(int X, int Y)> _MacroblockOrder() {
    var fullColumns = this._width / 16;
    var fullRows = this._height / 16;

    for (var y = 0; y < fullRows; ++y)
      for (var x = 0; x < fullColumns; ++x)
        yield return (x, y);

    if (fullColumns < this._macroblockWidth)
      for (var y = 0; y < fullRows; ++y)
        yield return (fullColumns, y);

    if (fullRows < this._macroblockHeight)
      for (var x = 0; x < this._macroblockWidth; ++x)
        yield return (x, fullRows);
  }
}
