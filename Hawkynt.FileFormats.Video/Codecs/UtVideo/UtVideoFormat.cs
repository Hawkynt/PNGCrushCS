using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Codecs.UtVideo;

/// <summary>What a Ut Video stream's samples mean.</summary>
internal enum UtVideoColourSpace {

  /// <summary>Green, blue and red planes, full range.</summary>
  Rgb,

  /// <summary>Green, blue, red and alpha planes, full range.</summary>
  Rgba,

  /// <summary>Luminance and two chrominance planes, studio swing.</summary>
  Yuv,
}

/// <summary>The four ways a sample can be predicted from the ones already decoded.</summary>
/// <remarks>
/// Numbered as the frame trailer numbers them. Public because the encoder takes one: which
/// predictor a frame uses is the encoder's choice and is not in the stream description, so there is
/// no other way to ask for it.
/// </remarks>
public enum UtVideoPredictor {

  /// <summary>None: the coded symbol is the sample.</summary>
  None = 0,

  /// <summary>The sample to the left, running on across the whole slice.</summary>
  Left = 1,

  /// <summary>Left plus above less above-left.</summary>
  Gradient = 2,

  /// <summary>The median of the left, the above, and the gradient of the two.</summary>
  Median = 3,
}

/// <summary>
/// What a Ut Video stream states about itself: its colour space, its subsampling, how many slices a
/// frame is cut into, and whether it is coded at all.
/// </summary>
/// <remarks>
/// Almost all of it is in sixteen bytes of stream description behind the <c>BITMAPINFOHEADER</c>,
/// and the layout of those is published: an encoder version, the four-character code of whatever the
/// picture was before it was coded, the size of the per-frame trailer, and a word of flags. The
/// flags carry the slice count in their top byte, less one, and two bits that matter — one saying
/// the frame is Huffman coded and one saying the source was interlaced.
/// <para/>
/// The prediction method is not here. It is in the trailer at the end of every frame, so a stream
/// may in principle change predictor between frames; this reads it per frame rather than assuming it
/// is fixed.
/// </remarks>
internal sealed class UtVideoFormat {

  /// <summary>How many bytes of stream description the format needs behind the bitmap header.</summary>
  private const int _EXTRA_SIZE = 16;

  /// <summary>The flag that says a frame is Huffman coded rather than coded some other way.</summary>
  private const uint _HUFFMAN = 0x00000001;

  /// <summary>The flag that says the picture is two interleaved fields.</summary>
  private const uint _INTERLACED = 0x00000800;

  private UtVideoFormat(
    UtVideoColourSpace colourSpace, int planeCount, int chromaHorizontalShift, int chromaVerticalShift,
    bool isBt709, int sliceCount, int frameInfoSize) {
    this.ColourSpace = colourSpace;
    this.PlaneCount = planeCount;
    this.ChromaHorizontalShift = chromaHorizontalShift;
    this.ChromaVerticalShift = chromaVerticalShift;
    this.IsBt709 = isBt709;
    this.SliceCount = sliceCount;
    this.FrameInfoSize = frameInfoSize;
  }

  internal UtVideoColourSpace ColourSpace { get; }
  internal int PlaneCount { get; }
  internal int ChromaHorizontalShift { get; }
  internal int ChromaVerticalShift { get; }

  /// <summary>Whether the chrominance is BT.709's rather than BT.601's, which the code says.</summary>
  internal bool IsBt709 { get; }

  internal int SliceCount { get; }

  /// <summary>How many bytes of trailer sit at the end of every frame.</summary>
  internal int FrameInfoSize { get; }

  internal bool HasAlpha => this.ColourSpace == UtVideoColourSpace.Rgba;

  /// <summary>
  /// Reads the stream description, refusing by name every spelling of the codec this cannot decode.
  /// </summary>
  internal static UtVideoFormat Parse(CodecTag codec, ReadOnlySpan<byte> extra, int streamIndex) {
    var name = codec.ToString();
    _RefuseFamiliesWithNoPublishedBitstream(name, streamIndex);

    var (colourSpace, planes, hshift, vshift, bt709) = _Layout(name, streamIndex);

    if (extra.Length < _EXTRA_SIZE)
      throw new InvalidDataException(
        $"Video stream {streamIndex} is {name} but carries {extra.Length} bytes of stream description behind its bitmap header, where {_EXTRA_SIZE} are needed to say how many slices a frame has.");

    var frameInfoSize = (int)_ReadUInt32(extra, 8);
    var flags = _ReadUInt32(extra, 12);

    if (frameInfoSize is < 1 or > 8)
      throw new InvalidDataException(
        $"Video stream {streamIndex} states a per-frame trailer of {frameInfoSize} bytes, which is not a size the format uses.");

    if ((flags & _HUFFMAN) == 0)
      throw new NotSupportedException(
        $"Video stream {streamIndex} is {name} coded without Huffman coding. Newer encoders can code a frame with finite state entropy coding instead — the mode the codec's author calls fsemedian — and that bitstream is not published anywhere and is not read here. The Huffman modes, which every encoder before version 23 wrote and which remain the default, are read.");

    if ((flags & _INTERLACED) != 0)
      throw new NotSupportedException(
        $"Video stream {streamIndex} is {name} with the interlace flag set. How the flag reorders a frame's rows is stated nowhere, and no encoder reachable here writes one, so there is no file against which a reading of it could be measured. It is refused rather than decoded as though it were progressive.");

    var sliceCount = (int)((flags >> 24) & 0xFF) + 1;

    return new(colourSpace, planes, hshift, vshift, bt709, sliceCount, frameInfoSize);
  }

  /// <summary>
  /// The layout an encoder is going to write, from the code alone and the choices it has made.
  /// </summary>
  /// <remarks>
  /// The encoder's side of <see cref="Parse"/>: the same table of codes, but the slice count and
  /// trailer size are what the encoder decided rather than what a description states, and the
  /// description is written from the result rather than read into it.
  /// </remarks>
  internal static UtVideoFormat ForEncoding(CodecTag codec, int sliceCount, int frameInfoSize, int streamIndex) {
    var (colourSpace, planes, hshift, vshift, bt709) = _Layout(codec.ToString(), streamIndex);
    return new(colourSpace, planes, hshift, vshift, bt709, sliceCount, frameInfoSize);
  }

  /// <summary>The sixteen bytes an encoder puts behind the bitmap header to describe this.</summary>
  /// <remarks>
  /// The first word is an encoder version whose last byte is an implementation identifier the
  /// format's author hands out; the second names what the picture was before it was coded and is
  /// read by nothing. Both are written as libavcodec writes them, since every decoder measured
  /// against ignores them and a value nobody has seen is a worse choice than one everybody has.
  /// </remarks>
  internal byte[] Describe() {
    var original = this.ColourSpace switch {
      UtVideoColourSpace.Rgb => new byte[] { 0x00, 0x00, 0x01, 0x18 },
      UtVideoColourSpace.Rgba => new byte[] { 0x00, 0x00, 0x02, 0x18 },
      _ when this.ChromaVerticalShift > 0 => "YV12"u8.ToArray(),
      _ when this.ChromaHorizontalShift > 0 => "YUY2"u8.ToArray(),
      _ => "YV24"u8.ToArray(),
    };

    var flags = _HUFFMAN | ((uint)(this.SliceCount - 1) << 24);
    var extra = new byte[_EXTRA_SIZE];
    extra[0] = 0xF0;
    extra[3] = 0x01;
    original.CopyTo(extra, 4);
    _WriteUInt32(extra, 8, (uint)this.FrameInfoSize);
    _WriteUInt32(extra, 12, flags);
    return extra;
  }

  /// <summary>
  /// The prediction method, which is in the last bytes of a frame rather than in the description.
  /// </summary>
  internal UtVideoPredictor PredictorOf(ReadOnlySpan<byte> frame, int streamIndex) {
    if (frame.Length < this.FrameInfoSize)
      throw new InvalidDataException(
        $"A frame of {frame.Length} bytes has no room for the {this.FrameInfoSize}-byte trailer that states how its samples are predicted.");

    var info = 0u;
    for (var i = 0; i < this.FrameInfoSize && i < 4; ++i)
      info |= (uint)frame[frame.Length - this.FrameInfoSize + i] << (i * 8);

    var predictor = (UtVideoPredictor)((info >> 8) & 3);
    return predictor;
  }

  /// <summary>Where slice <paramref name="index"/> starts, in the rows of one plane.</summary>
  /// <remarks>
  /// A frame is cut at <c>height * index / slices</c>, which its description states. What that
  /// description does not state is what happens when the cut lands between the two luminance rows
  /// that share one chrominance row, and a 4:2:0 frame cannot be cut there. The cut is rounded down
  /// to a whole chrominance row and the luminance boundary follows from it, rather than each plane
  /// being divided on its own height.
  /// <para/>
  /// The two readings agree on every frame whose boundaries already land on even rows, which is why
  /// this only shows itself on a slice count that does not divide the height — a 98-row frame in
  /// three slices, say, where dividing each plane separately puts the luminance boundary at row 65
  /// and the chrominance boundary at row 32, and the two no longer describe the same band of
  /// picture. Both planes then decode into rubbish from that slice on.
  /// </remarks>
  internal int SliceStart(int index, int frameHeight, int planeVerticalShift) {
    var shift = this.ChromaVerticalShift;
    var whole = (frameHeight * index / this.SliceCount) >> shift;
    return whole << (shift - planeVerticalShift);
  }

  /// <summary>Refuses the relatives of this codec whose coding is not published.</summary>
  private static void _RefuseFamiliesWithNoPublishedBitstream(string name, int streamIndex) {
    if (name.Length == 4 && name[0] == 'U' && name[1] == 'Q')
      throw new NotSupportedException(
        $"Video stream {streamIndex} is {name}, one of the ten-bit Ut Video Pro codes. The eight-bit family's frame layout is published; the Pro family's is not, and no encoder reachable here writes one, so nothing could be measured against. It is refused by name rather than read as though its samples were bytes.");

    if (name.Length == 4 && name[0] == 'U' && name[1] == 'M')
      throw new NotSupportedException(
        $"Video stream {streamIndex} is {name}, one of the Ut Video T2 codes. T2 is a different codec that happens to share a name: it does not use Huffman coding and it codes some frames against the frame before, neither of which the eight-bit family does. Its bitstream is not published and it is refused by name.");
  }

  /// <summary>The plane layout each four-character code stands for.</summary>
  /// <remarks>
  /// The codes are the one part of this format its author documents completely. <c>R</c> is colour
  /// and <c>RA</c> colour with transparency, both full range; the digit after <c>Y</c> or <c>H</c>
  /// is the chroma subsampling, and the letter is which primaries the chrominance is against —
  /// <c>Y</c> for BT.601 and <c>H</c> for BT.709. That is the whole of the difference between
  /// <c>ULY2</c> and <c>ULH2</c>: the same bits, read against a different matrix.
  /// </remarks>
  private static (UtVideoColourSpace, int, int, int, bool) _Layout(string name, int streamIndex) => name switch {
    "ULRG" => (UtVideoColourSpace.Rgb, 3, 0, 0, false),
    "ULRA" => (UtVideoColourSpace.Rgba, 4, 0, 0, false),
    "ULY0" => (UtVideoColourSpace.Yuv, 3, 1, 1, false),
    "ULY2" => (UtVideoColourSpace.Yuv, 3, 1, 0, false),
    "ULY4" => (UtVideoColourSpace.Yuv, 3, 0, 0, false),
    "ULH0" => (UtVideoColourSpace.Yuv, 3, 1, 1, true),
    "ULH2" => (UtVideoColourSpace.Yuv, 3, 1, 0, true),
    "ULH4" => (UtVideoColourSpace.Yuv, 3, 0, 0, true),
    _ => throw new NotSupportedException(
      $"Video stream {streamIndex} is named {name}, which is not a Ut Video code this reads."),
  };

  private static uint _ReadUInt32(ReadOnlySpan<byte> source, int offset)
    => (uint)(source[offset] | (source[offset + 1] << 8) | (source[offset + 2] << 16) | (source[offset + 3] << 24));

  private static void _WriteUInt32(Span<byte> target, int offset, uint value) {
    target[offset] = (byte)value;
    target[offset + 1] = (byte)(value >> 8);
    target[offset + 2] = (byte)(value >> 16);
    target[offset + 3] = (byte)(value >> 24);
  }
}
