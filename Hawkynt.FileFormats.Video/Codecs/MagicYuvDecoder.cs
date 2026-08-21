using System;
using System.IO;
using FileFormat.Codecs.MagicYuv;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>
/// Decodes MagicYUV, a lossless capture codec: Huffman coding over a spatial prediction, with the
/// frame cut into slices that decode independently of one another.
/// </summary>
/// <remarks>
/// Lossless and intra only — every frame stands alone. The shape is the family's: a sample is
/// predicted from its neighbours, the difference is Huffman coded with one table a plane, and the
/// plane is cut into horizontal bands that share the table but nothing else.
/// <para/>
/// <b>Almost nothing about the bitstream is published.</b> It is a commercial codec with no
/// specification and no format note. What is public is a list of its four-character codes and their
/// pixel formats, from its author; that it is intra only and cut into slices, also from him; and
/// that its Huffman codes are transmitted as one length a symbol with the longer codes to the left
/// of the tree and the symbols of one length ascending, which is stated in ffmpeg's commit messages
/// rather than its source. Everything else this decoder does was established by measuring frames
/// against the pictures they were made from — a frame layout found by arithmetic, the code
/// assignment found by the shape of a flat picture's slice data, and the predictors found by where a
/// decode first went wrong. Each is recorded at the place it is used, with what reading it the other
/// way does to a picture.
/// <para/>
/// <b>The frame carries its own header, and the stream description is ignored.</b> The sixteen bytes
/// an AVI holds behind the <c>BITMAPINFOHEADER</c> are a copy of the frame's own first bytes, so
/// there is nothing in the container this needs. The picture size, the slice height, the tables and
/// the prediction all come out of the frame.
/// <para/>
/// <b>Every row starts again from the sample above it.</b> Not from the end of the row before, which
/// is what HuffYUV and Ut Video do. See <see cref="MagicYuvPrediction"/>.
/// <para/>
/// <b>The bits are plain bytes</b>, most significant first, with none of the little-endian word
/// swapping the two codecs it most resembles need. See <see cref="MagicYuvBitReader"/>.
/// <para/>
/// <b>A slice may be stored uncompressed</b>, and says so in its first byte. The prediction still
/// applies; only the entropy coding is skipped. Sixty-four streams of the corpus contain one, so it
/// is not an edge case — a frame small enough that a Huffman table costs more than it saves produces
/// them, and so does a slice of noise.
/// <para/>
/// <b>Every slice states the prediction, and every slice of a frame states the same one.</b> The
/// codec's author has described the choice publicly as one made once a frame, and that is what the
/// files show. The field is read per slice all the same, because that is where it sits — reading it
/// once would be asserting something about the encoder rather than about the format.
/// <para/>
/// <b>Measured against the pictures the frames were made from.</b> 309 streams and 1,446 frames.
/// The ffmpeg built here has MagicYUV's encoder but not its decoder, so the oracle is the rawvideo
/// that went into the encoder rather than another decoder's opinion of what came out — which for a
/// lossless codec is the stronger of the two, being the ground truth itself. Every sample of every
/// plane of every frame is identical: all seven pixel formats its encoder writes, all three
/// predictors, slice counts of one to eight, and sizes from 1x1 to 320x240 including odd widths and
/// heights and slice counts that exceed the number of rows.
/// <para/>
/// <b>What refuses.</b> A four-character code outside the eight-bit family, grey with alpha, a
/// version or header size other than the one measured, a frame without its signature, a code-length
/// table that does not describe a complete code, a slice whose first byte is neither of the two
/// values that mean anything, and a frame whose parts do not add up. There is no <c>catch</c> here
/// handing back a blank or a repeated frame.
/// </remarks>
public sealed class MagicYuvDecoder : IVideoCodecDecoder<MagicYuvDecoder> {

  /// <summary>The codes this decoder answers to, including those it answers only to refuse.</summary>
  private static readonly CodecTag[] _Tags = [
    CodecTag.FromCharacters("M8RG"),
    CodecTag.FromCharacters("M8RA"),
    CodecTag.FromCharacters("M8Y0"),
    CodecTag.FromCharacters("M8Y2"),
    CodecTag.FromCharacters("M8Y4"),
    CodecTag.FromCharacters("M8YA"),
    CodecTag.FromCharacters("M8G0"),
    CodecTag.FromCharacters("M8GA"),
    CodecTag.FromCharacters("MAGY"),
    CodecTag.FromCharacters("M0RG"),
    CodecTag.FromCharacters("M0RA"),
    CodecTag.FromCharacters("M0Y0"),
    CodecTag.FromCharacters("M0Y2"),
    CodecTag.FromCharacters("M0Y4"),
    CodecTag.FromCharacters("M0G0"),
    CodecTag.FromCharacters("M2RG"),
    CodecTag.FromCharacters("M2RA"),
    CodecTag.FromCharacters("M4RG"),
    CodecTag.FromCharacters("M4RA"),
  ];

  /// <summary>The two values a slice's first byte takes.</summary>
  private const byte _CODED = 0;
  private const byte _UNCOMPRESSED = 1;

  private readonly int _width;
  private readonly int _height;
  private readonly int _streamIndex;
  private readonly MagicYuvFormat _format;

  private MagicYuvDecoder(int width, int height, int streamIndex, MagicYuvFormat format) {
    this._width = width;
    this._height = height;
    this._streamIndex = streamIndex;
    this._format = format;
  }

  public static string CodecName => "MagicYUV";

  public static bool Accepts(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);

    if (stream.Kind != MediaStreamKind.Video)
      return false;

    foreach (var tag in _Tags)
      if (stream.Codec.EqualsIgnoringCase(tag))
        return true;

    return false;
  }

  /// <summary>
  /// Builds a decoder from the code and the picture size, which is all the container has to give.
  /// </summary>
  public static MagicYuvDecoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);

    var format = MagicYuvFormat.Of(stream.Codec, stream.Index);

    if (stream.Width <= 0 || stream.Height <= 0)
      throw new InvalidDataException(
        $"Video stream {stream.Index} states a picture size of {stream.Width}x{stream.Height}, which no frame can be decoded into.");

    return new(stream.Width, stream.Height, stream.Index, format);
  }

  /// <summary>Decodes one frame, which for this codec is always exactly one whole picture.</summary>
  public bool TryDecode(CodedPacket packet, out RawImage frame) {
    frame = this._Compose(this.DecodePlanes(packet.Data));
    return true;
  }

  /// <summary>
  /// Decodes one frame as far as its planes, before anything is made of their colour.
  /// </summary>
  /// <remarks>
  /// Split out from <see cref="TryDecode"/> so that the planes can be measured as planes. A
  /// subsampled picture's samples do not survive being turned into pixels, and comparing the result
  /// against another decoder's would be comparing two conventions rather than two decodings.
  /// </remarks>
  internal byte[][] DecodePlanes(ReadOnlyMemory<byte> frame) {
    var data = frame.Span;
    var format = this._format;

    if (data.Length < MagicYuvFormat.HEADER_SIZE || !data[..4].SequenceEqual(MagicYuvFormat.Signature))
      throw new InvalidDataException(
        $"A frame of {data.Length} bytes does not begin with this codec's four-byte signature, so it is not one of its frames.");

    var headerSize = (int)_ReadUInt32(data, 4);
    if (headerSize != MagicYuvFormat.HEADER_SIZE)
      throw new NotSupportedException(
        $"Video stream {this._streamIndex} states a frame header of {headerSize} bytes where every file measured states {MagicYuvFormat.HEADER_SIZE}. What a header of another size carries is not published and could not be measured, so it is refused rather than read as though the fields sat where they usually do.");

    var version = data[8];
    if (version != MagicYuvFormat.VERSION_BYTE)
      throw new NotSupportedException(
        $"Video stream {this._streamIndex} has {version} in the byte that is {MagicYuvFormat.VERSION_BYTE} in every frame measured here. What that byte means is not published — its position suggests a version and nothing states one — so a frame holding another value is one nothing was measured against, and it is refused rather than read on the assumption that the rest of the header is unchanged.");

    var statedWidth = (int)_ReadUInt32(data, 16);
    var statedHeight = (int)_ReadUInt32(data, 20);
    if (statedWidth != this._width || statedHeight != this._height)
      throw new InvalidDataException(
        $"A frame states a picture of {statedWidth}x{statedHeight} where video stream {this._streamIndex} states {this._width}x{this._height}.");

    // The byte after the format is the longest code the frame's tables use — 12 in every
    // eight-bit frame measured, which is also the longest length any of their tables gives.
    var longestCode = data[10];
    if (longestCode is < 1 or > 32)
      throw new InvalidDataException(
        $"A frame states {longestCode} as the longest code its tables use, which no Huffman table can be built to.");

    var sliceHeight = (int)_ReadUInt32(data, 28);
    if (sliceHeight <= 0)
      throw new InvalidDataException($"A frame states a slice height of {sliceHeight}.");

    // A 4:2:0 frame cannot be cut between the two luminance rows that share a chrominance row.
    if ((sliceHeight & ((1 << format.ChromaVerticalShift) - 1)) != 0)
      throw new InvalidDataException(
        $"A frame states a slice height of {sliceHeight}, which does not divide by the {1 << format.ChromaVerticalShift} luminance rows its chrominance rows each cover, so its slices would not line up between the planes.");

    var slices = (this._height + sliceHeight - 1) / sliceHeight;
    var planes = format.PlaneCount;
    var pieces = planes * slices;

    var at = headerSize;
    if (at + 4 * (pieces + 1) > data.Length)
      throw new InvalidDataException(
        $"A frame of {data.Length} bytes ends inside the {pieces + 1} offsets its {planes} planes and {slices} slices need.");

    var offsets = new int[pieces + 1];
    for (var i = 0; i <= pieces; ++i) {
      offsets[i] = (int)_ReadUInt32(data, at);
      at += 4;
    }

    if (at >= data.Length)
      throw new InvalidDataException("A frame ends before it says how many code-length tables it carries.");

    var tableCount = data[at];
    ++at;
    if (tableCount != planes)
      throw new InvalidDataException(
        $"A frame carries {tableCount} code-length tables where its {planes} planes need one each.");

    var order = this._ReadOrder(data, ref at, pieces, planes, slices);

    var tables = new MagicYuvHuffmanTable[tableCount];
    for (var i = 0; i < tableCount; ++i) {
      if (at + MagicYuvHuffmanTable.SYMBOL_COUNT > data.Length)
        throw new InvalidDataException($"A frame ends inside code-length table {i}.");

      tables[i] = new(data.Slice(at, MagicYuvHuffmanTable.SYMBOL_COUNT), i, longestCode);
      at += MagicYuvHuffmanTable.SYMBOL_COUNT;
    }

    if (at != MagicYuvFormat.HEADER_SIZE + offsets[0])
      throw new InvalidDataException(
        $"A frame's tables end at byte {at} where it states its first slice begins at {MagicYuvFormat.HEADER_SIZE + offsets[0]}.");

    return this._DecodePlanes(frame, data, offsets, order, tables, slices, sliceHeight);
  }

  /// <summary>
  /// Reads the map between the offsets and the pieces they belong to.
  /// </summary>
  /// <remarks>
  /// <b>It runs the other way from the obvious one.</b> Its <c>k</c>-th entry names the piece the
  /// <c>k</c>-th offset belongs to, rather than naming the offset the <c>k</c>-th piece uses, and a
  /// piece is named by <c>slice * planes + plane</c>. On a frame of one slice the two readings are
  /// the same permutation, so a single-slice frame decodes perfectly either way and every other
  /// frame comes apart — which is exactly how it was found.
  /// </remarks>
  private int[] _ReadOrder(ReadOnlySpan<byte> data, ref int at, int pieces, int planes, int slices) {
    if (at + pieces > data.Length)
      throw new InvalidDataException($"A frame ends inside the {pieces}-byte map of its slices.");

    var where = new int[pieces];
    for (var i = 0; i < pieces; ++i)
      where[i] = -1;

    for (var k = 0; k < pieces; ++k) {
      var piece = data[at + k];
      if (piece >= pieces)
        throw new InvalidDataException(
          $"A frame's slice map names piece {piece} where it has {pieces} of them.");

      if (where[piece] >= 0)
        throw new InvalidDataException($"A frame's slice map names piece {piece} twice.");

      where[piece] = k;
    }

    at += pieces;
    return where;
  }

  private byte[][] _DecodePlanes(
    ReadOnlyMemory<byte> frame, ReadOnlySpan<byte> data, int[] offsets, int[] where,
    MagicYuvHuffmanTable[] tables, int slices, int frameSliceHeight) {
    var format = this._format;
    var planes = new byte[format.PlaneCount][];

    for (var plane = 0; plane < format.PlaneCount; ++plane) {
      var (width, height) = format.PlaneSize(plane, this._width, this._height);
      var sliceHeight = format.SliceHeight(plane, frameSliceHeight);
      var samples = new byte[width * height];

      for (var slice = 0; slice < slices; ++slice) {
        var firstRow = Math.Min(slice * sliceHeight, height);
        var lastRow = Math.Min(firstRow + sliceHeight, height);
        if (lastRow <= firstRow)
          continue;

        var index = where[slice * format.PlaneCount + plane];
        var start = MagicYuvFormat.HEADER_SIZE + offsets[index + 1];
        var end = _EndOf(offsets, start, data.Length);
        if (start + 2 > end || end > data.Length)
          throw new InvalidDataException(
            $"Plane {plane} slice {slice} runs from byte {start} to {end} of a frame of {data.Length} bytes.");

        this._DecodeSlice(
          frame, data, start, end, tables[plane], samples, width, firstRow, lastRow, plane, slice);
      }

      planes[plane] = samples;
    }

    // The colour modes carry the planes blue, green, red — green plainly and the other two as their
    // distance from it, which is where most of their compression comes from since a picture is
    // mostly grey. An alpha plane is not decorrelated: adding green to it would make an opaque
    // picture translucent wherever it is dark.
    if (format.ColourSpace == MagicYuvColourSpace.Rgb) {
      var blue = planes[0];
      var green = planes[1];
      var red = planes[2];
      for (var i = 0; i < green.Length; ++i) {
        var g = green[i];
        blue[i] = (byte)(blue[i] + g);
        red[i] = (byte)(red[i] + g);
      }

      planes = format.HasAlpha
        ? [green, blue, red, planes[3]]
        : [green, blue, red];
    }

    return planes;
  }

  /// <summary>Where a piece ends, which is at whichever piece starts next.</summary>
  private static int _EndOf(int[] offsets, int start, int frameLength) {
    var end = frameLength;
    for (var i = 1; i < offsets.Length; ++i) {
      var candidate = MagicYuvFormat.HEADER_SIZE + offsets[i];
      if (candidate > start && candidate < end)
        end = candidate;
    }

    return end;
  }

  private void _DecodeSlice(
    ReadOnlyMemory<byte> frame, ReadOnlySpan<byte> data, int start, int end,
    MagicYuvHuffmanTable table, byte[] samples, int width, int firstRow, int lastRow,
    int plane, int slice) {
    var flag = data[start];
    var predictor = (MagicYuvPredictor)data[start + 1];
    if (predictor is not (MagicYuvPredictor.Left or MagicYuvPredictor.Gradient or MagicYuvPredictor.Median))
      throw new InvalidDataException(
        $"Plane {plane} slice {slice} states prediction method {data[start + 1]}, which is none of the three the format has.");

    var from = firstRow * width;
    var count = (lastRow - firstRow) * width;

    switch (flag) {
      case _CODED: {
        var bits = new MagicYuvBitReader(frame.Slice(start + 2, end - start - 2));
        for (var i = 0; i < count; ++i)
          samples[from + i] = (byte)table.Read(bits);

        break;
      }

      case _UNCOMPRESSED: {
        if (start + 2 + count > end)
          throw new InvalidDataException(
            $"Plane {plane} slice {slice} is stored uncompressed but holds {end - start - 2} bytes where its {lastRow - firstRow} rows need {count}.");

        data.Slice(start + 2, count).CopyTo(samples.AsSpan(from, count));
        break;
      }

      default:
        throw new InvalidDataException(
          $"Plane {plane} slice {slice} opens with the byte {flag}, which is neither the nought that means it is coded nor the one that means it is stored plainly.");
    }

    MagicYuvPrediction.Apply(samples, width, firstRow, lastRow, predictor);
  }

  // ============================================================================================
  // What comes out
  // ============================================================================================

  private RawImage _Compose(byte[][] planes) => this._format.ColourSpace switch {
    MagicYuvColourSpace.Grey => new() {
      Width = this._width,
      Height = this._height,
      Format = PixelFormat.Gray8,
      PixelData = planes[0],
    },
    MagicYuvColourSpace.Rgb => this._FromColour(planes),
    _ => this._FromYuv(planes),
  };

  private RawImage _FromColour(byte[][] planes) {
    var count = this._width * this._height;
    var green = planes[0];
    var blue = planes[1];
    var red = planes[2];
    var hasAlpha = this._format.HasAlpha;
    var channels = hasAlpha ? 4 : 3;
    var alpha = hasAlpha ? planes[3] : null;
    var pixels = new byte[count * channels];

    for (var i = 0; i < count; ++i) {
      var at = i * channels;
      pixels[at] = red[i];
      pixels[at + 1] = green[i];
      pixels[at + 2] = blue[i];
      if (alpha != null)
        pixels[at + 3] = alpha[i];
    }

    return new() {
      Width = this._width,
      Height = this._height,
      Format = hasAlpha ? PixelFormat.Rgba32 : PixelFormat.Rgb24,
      PixelData = pixels,
    };
  }

  /// <summary>
  /// Turns the luminance and chrominance planes into the packed colour every reader here hands back.
  /// </summary>
  /// <remarks>
  /// The conversion is a display convention and not part of the coding. The one used here is ITU-R
  /// BT.601 with studio swing — luminance running 16 to 235 rather than filling the byte — and each
  /// chrominance sample repeated across the block it covers, which is what a subsampled picture's
  /// samples mean.
  /// <para/>
  /// <b>BT.601 is an assumption, and one this could not test.</b> Unlike Ut Video, none of the codes
  /// distinguishes the two sets of primaries; the codec's author has said publicly that which one a
  /// file uses is carried inside the stream, but no field of the header changes when an encoder here
  /// is asked for BT.709 — its encoder simply never writes one. So there is no file against which a
  /// reading of that field could be found, and the choice is stated here rather than pretended at.
  /// It affects only the pixels this hands back and none of the samples the codec actually codes,
  /// which is why the comparison that measures this decoder is made on the planes.
  /// </remarks>
  private RawImage _FromYuv(byte[][] planes) {
    var format = this._format;
    var luma = planes[0];
    var cb = planes[1];
    var cr = planes[2];
    var hasAlpha = format.HasAlpha;
    var alpha = hasAlpha ? planes[3] : null;
    var channels = hasAlpha ? 4 : 3;
    var (chromaWidth, chromaHeight) = format.PlaneSize(1, this._width, this._height);
    var pixels = new byte[this._width * this._height * channels];

    for (var y = 0; y < this._height; ++y) {
      var chromaRow = Math.Min(y >> format.ChromaVerticalShift, chromaHeight - 1);
      var lumaRow = y * this._width;
      var target = lumaRow * channels;

      for (var x = 0; x < this._width; ++x) {
        var chromaColumn = Math.Min(x >> format.ChromaHorizontalShift, chromaWidth - 1);
        var at = chromaRow * chromaWidth + chromaColumn;

        var scaledLuma = 298 * (luma[lumaRow + x] - 16);
        var blueDifference = cb[at] - 128;
        var redDifference = cr[at] - 128;

        pixels[target] = _Clamp(scaledLuma + 409 * redDifference + 128);
        pixels[target + 1] = _Clamp(scaledLuma - 100 * blueDifference - 208 * redDifference + 128);
        pixels[target + 2] = _Clamp(scaledLuma + 516 * blueDifference + 128);
        if (alpha != null)
          pixels[target + 3] = alpha[lumaRow + x];

        target += channels;
      }
    }

    return new() {
      Width = this._width,
      Height = this._height,
      Format = hasAlpha ? PixelFormat.Rgba32 : PixelFormat.Rgb24,
      PixelData = pixels,
    };
  }

  private static byte _Clamp(int scaled) {
    var value = scaled >> 8;
    return (byte)(value < 0 ? 0 : value > 255 ? 255 : value);
  }

  private static uint _ReadUInt32(ReadOnlySpan<byte> source, int offset)
    => (uint)(source[offset] | (source[offset + 1] << 8) | (source[offset + 2] << 16) | (source[offset + 3] << 24));
}
