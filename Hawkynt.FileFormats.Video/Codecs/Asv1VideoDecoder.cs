using System;
using System.Collections.Generic;
using System.IO;
using FileFormat.Bmp;
using FileFormat.Codecs.Asv1;
using FileFormat.Codecs.H263;
using FileFormat.Codecs.Mpeg;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>
/// Decodes ASUS V1, an AVI-native intra-only DCT codec ASUSTeK wrote for its own TV tuner cards, coded
/// from Michael Niedermayer's "ASUS V1/V2 Codecs" (asv1.txt, 2003-2016, GNU FDL/GPL) — a genuine
/// bitstream specification, not a paraphrase of an implementation: every field, every VLC table and
/// the dequantisation formula are given in full, with a changelog naming two authors across six
/// revisions and a worked example decoder that is exactly how a reader independent of that document's
/// own prose confirms it was read correctly.
/// </summary>
/// <remarks>
/// A macroblock is 16x16 luma as four 8x8 blocks (Y0 top-left, Y1 top-right, Y2 bottom-left, Y3
/// bottom-right) and one 8x8 block each of Cb and Cr — the same 4:2:0 shape and the same quadrant
/// order ITU-T H.263 uses, which is why this decoder reuses that codec's picture buffer, its inverse
/// transform, and its studio-range 4:2:0-to-RGB conversion rather than writing new copies of any of
/// them: <c>H263Frame</c>, <c>H263InverseDct</c> and <c>H263ColorConversion</c>. Everything else is
/// ASV1's own: a per-file quantisation parameter carried in the stream's own codec-private data rather
/// than in every packet, coefficients dequantised against ISO/IEC 11172-2's own intra matrix
/// (<c>MpegQuantisation.DefaultIntraMatrix</c>) scaled by a factor of sixty-four, sixty-four samples a
/// block read as a DC field and up to ten coefficient groups of four, and — the one genuinely unusual
/// part — a packet stored with every four-byte word's byte order reversed before any of that can be
/// read at all (<see cref="Asv1Bitstream.SwapWords"/>).
/// <para/>
/// Every picture is coded independently: there is no motion compensation and no prediction between
/// pictures of any kind, so a packet decodes to exactly one picture on its own and nothing is ever
/// held back across calls.
/// <para/>
/// <b>Two things the document states in words and this settles by measurement.</b> Its two coordinate
/// diagrams — the sixteen coefficient groups across a block, and the four positions within one group —
/// are drawn as a page reads, left to right then top to bottom, but a real encoded picture only
/// reconstructs when the row and column each diagram states are swapped, at both levels at once; see
/// the remarks on <see cref="Asv1VlcTables.ScanPosition"/>. And "byte-swapped 32-bit words" is exactly
/// that and nothing subtler — reversing each four-byte group once up front leaves an ordinary
/// most-significant-bit-first reader for everything after it, confirmed because doing so recovers the
/// document's own DC identity (dequantised DC divided back down by the inverse transform's DC-only
/// path reproduces the coded field exactly) on a real flat frame's every block, luma and chroma alike.
/// <para/>
/// <b>Right column and bottom row.</b> A picture whose size is not a whole number of macroblocks codes
/// the macroblocks that are, in raster order, then the partial-width column top to bottom, then the
/// partial-height row — including its own corner — left to right (clause 3.1's own worked example).
/// A picture past the coded size is cropped rather than shown, the same way <c>H263Frame</c> already
/// keeps a whole-macroblock canvas underneath a picture that is not one.
/// <para/>
/// There is no <c>catch</c> anywhere that hands back a blank, a copied or a zero-filled picture.
/// </remarks>
public sealed class Asv1VideoDecoder : IVideoCodecDecoder<Asv1VideoDecoder> {

  private static readonly CodecTag _Tag = CodecTag.FromCharacters("ASV1");

  /// <summary>How many bytes of codec-private data ASV1's own global header needs behind the BITMAPINFOHEADER.</summary>
  private const int _GlobalHeaderSize = 8;

  private readonly int _width;
  private readonly int _height;
  private readonly int _macroblockWidth;
  private readonly int _macroblockHeight;
  private readonly int[] _dequantFactors;
  private readonly int _streamIndex;

  private Asv1VideoDecoder(int width, int height, int quantiser, int streamIndex) {
    this._width = width;
    this._height = height;
    this._macroblockWidth = (width + 15) / 16;
    this._macroblockHeight = (height + 15) / 16;
    this._streamIndex = streamIndex;

    // Clause 3.6: floor(D * q[i] / QP), D = 64 for ASV1, built once from the file's one quantiser.
    var factors = new int[64];
    for (var i = 0; i < 64; ++i)
      factors[i] = 64 * MpegQuantisation.DefaultIntraMatrix[i] / quantiser;
    this._dequantFactors = factors;
  }

  /// <summary>Gets the codec name.</summary>
  public static string CodecName => "ASUS V1";

  /// <summary>Determines whether the specified media stream is supported.</summary>
  public static bool Accepts(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);

    return stream.Kind == MediaStreamKind.Video && stream.Codec.EqualsIgnoringCase(_Tag);
  }

  /// <summary>Creates a decoder for the specified media stream.</summary>
  public static Asv1VideoDecoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);

    if (stream.Width <= 0 || stream.Height <= 0)
      throw new InvalidDataException(
        $"Video stream {stream.Index} states a picture size of {stream.Width}x{stream.Height}, which no frame can "
        + "be decoded into.");

    var format = stream.CodecPrivateData.Span;
    if (format.Length < BitmapInfoHeader.StructSize + _GlobalHeaderSize)
      throw new InvalidDataException(
        $"Video stream {stream.Index} carries {format.Length} byte(s) behind its BITMAPINFOHEADER, where ASV1's own "
        + $"eight-byte global header (asv1.txt 4.2) needs at least {BitmapInfoHeader.StructSize + _GlobalHeaderSize}.");

    var quantiser = format[BitmapInfoHeader.StructSize];
    if (quantiser == 0)
      throw new InvalidDataException(
        $"Video stream {stream.Index} states an ASV1 quantisation parameter of zero, which the dequantisation of "
        + "asv1.txt 3.6 divides by.");

    return new(stream.Width, stream.Height, quantiser, stream.Index);
  }

  /// <summary>Attempts to decode the specified coded packet into a raw image frame.</summary>
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
  /// Decodes one packet to its reconstructed 4:2:0 planes, ahead of the colour conversion every
  /// caller of <see cref="TryDecode"/> gets instead. Internal so that a comparison against another
  /// decoder's planes never has to go through a colour conversion at all — the one place this
  /// package's own measurements insist on for a subsampled codec.
  /// </summary>
  internal H263Frame _DecodePlanes(CodedPacket packet) {
    var swapped = Asv1Bitstream.SwapWords(packet.Data.Span);
    var reader = new H263BitReader(swapped);
    var target = new H263Frame(this._macroblockWidth, this._macroblockHeight);
    Span<int> block = stackalloc int[64];

    foreach (var (mbX, mbY) in this._MacroblockOrder())
      for (var index = 0; index < 6; ++index) {
        Asv1BlockDecoder.Read(ref reader, block, this._dequantFactors);
        this._Store(target, mbX, mbY, index, block);
      }

    return target;
  }

  /// <summary>Nothing is ever held back: ASV1 has no prediction between pictures to reorder around.</summary>
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
