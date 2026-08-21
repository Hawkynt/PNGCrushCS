using System;
using System.IO;
using FileFormat.Bmp;
using FileFormat.Codecs.UtVideo;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>
/// Decodes Ut Video, the lossless codec of UMEZAWA Takeshi: Huffman coding over a prediction, in
/// six colour spaces, with the frame cut into slices that decode independently of one another.
/// </summary>
/// <remarks>
/// Lossless and intra only — every frame stands alone, which is what makes it a capture and editing
/// codec. There is no transform and no quantiser. A sample is predicted from its neighbours, the
/// difference is Huffman coded with one table a plane, and the plane is cut into horizontal bands
/// that share the table but nothing else, so that a decoder with four cores can use them.
/// <para/>
/// <b>What the format's own description gives, and what it does not.</b> The author publishes the
/// four-character codes and their colour spaces, and the community write-up gives the sixteen bytes
/// of stream description, the order of a plane's parts, the rule that a slice starts at
/// <c>height * index / slices</c>, and that codes are handed out from the longest length down. That
/// is where the published record stops. The bit order, the tie-break between symbols of one code
/// length, the value a slice's prediction starts from, how the median behaves at the edge of a
/// slice, and how a 4:2:0 frame's slice boundaries are rounded are all stated nowhere and were
/// established here by measurement against files — each one is written down at the place it is used,
/// with what reading it the other way does to a picture.
/// <para/>
/// <b>The bits are in little-endian words</b>, as HuffYUV's are, so every four bytes of a slice have
/// to be turned round before any of it decodes. See <see cref="UtVideoBitReader"/>.
/// <para/>
/// <b>The codes are handed out from the longest length down, and within a length from the highest
/// symbol down.</b> The first half of that is published; the second is not, and taking the symbols
/// the other way round decodes a plane's short codes correctly and every long one wrongly. See
/// <see cref="UtVideoHuffmanTable"/>.
/// <para/>
/// <b>Prediction runs over a slice linearly.</b> The sample left of column zero is the last sample
/// of the row above; nothing resets at the end of a row, only at the start of a slice. The first
/// row of a slice is predicted from the left alone, starting at 128. See
/// <see cref="UtVideoPrediction"/>.
/// <para/>
/// <b>Blue and red are stored as their distance from green, offset by 128.</b> The offset is the
/// part that is not published: without it every sample of both planes is out by exactly that much.
/// <para/>
/// <b>Measured against ffmpeg.</b> 126 streams and 774 frames: every pixel format its encoder
/// writes, each of the three predictors it offers, both colour-space spellings, slice counts of one,
/// two, three, four, five and eight, and sizes from 16x16 to 320x240 including several where the
/// slice count does not divide the height. Every stream is decoded here and by ffmpeg and compared
/// plane by plane against ffmpeg's own planes — no colour conversion in the comparison, so the
/// chroma siting of the subsampled formats cannot hide anything — and every sample of every plane of
/// every frame is identical.
/// <para/>
/// <b>What refuses.</b> The ten-bit Pro codes, the T2 codes, a frame coded with finite state entropy
/// coding rather than Huffman, an interlaced stream, a code-length table that does not describe a
/// complete code, a plane whose slices do not cover it, and a frame whose parts do not add up to its
/// length. There is no <c>catch</c> here handing back a blank or a repeated frame.
/// </remarks>
public sealed class UtVideoDecoder : IVideoCodecDecoder<UtVideoDecoder> {

  /// <summary>The codes this decoder answers to.</summary>
  private static readonly CodecTag[] _Tags = [
    CodecTag.FromCharacters("ULRG"),
    CodecTag.FromCharacters("ULRA"),
    CodecTag.FromCharacters("ULY0"),
    CodecTag.FromCharacters("ULY2"),
    CodecTag.FromCharacters("ULY4"),
    CodecTag.FromCharacters("ULH0"),
    CodecTag.FromCharacters("ULH2"),
    CodecTag.FromCharacters("ULH4"),
  ];

  /// <summary>
  /// The codes this decoder names in its refusal rather than leaving to no decoder at all.
  /// </summary>
  /// <remarks>
  /// They are accepted so that <see cref="Create"/> can say what is wrong with the stream. A caller
  /// that gets no decoder at all learns only that nothing matched, which for a file whose code
  /// plainly reads <c>UQY2</c> is a worse answer than "that is Ut Video Pro and its bitstream is not
  /// published".
  /// </remarks>
  private static readonly CodecTag[] _RefusedTags = [
    CodecTag.FromCharacters("UQRG"),
    CodecTag.FromCharacters("UQRA"),
    CodecTag.FromCharacters("UQY0"),
    CodecTag.FromCharacters("UQY2"),
    CodecTag.FromCharacters("UMRG"),
    CodecTag.FromCharacters("UMRA"),
    CodecTag.FromCharacters("UMY2"),
    CodecTag.FromCharacters("UMY4"),
    CodecTag.FromCharacters("UMH2"),
    CodecTag.FromCharacters("UMH4"),
  ];

  private readonly int _width;
  private readonly int _height;
  private readonly int _streamIndex;
  private readonly UtVideoFormat _format;

  private UtVideoDecoder(int width, int height, int streamIndex, UtVideoFormat format) {
    this._width = width;
    this._height = height;
    this._streamIndex = streamIndex;
    this._format = format;
  }

  public static string CodecName => "Ut Video";

  public static bool Accepts(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);

    if (stream.Kind != MediaStreamKind.Video)
      return false;

    foreach (var tag in _Tags)
      if (stream.Codec.EqualsIgnoringCase(tag))
        return true;

    foreach (var tag in _RefusedTags)
      if (stream.Codec.EqualsIgnoringCase(tag))
        return true;

    return false;
  }

  /// <summary>
  /// Builds a decoder from the stream description, which for this codec is a
  /// <c>BITMAPINFOHEADER</c> with sixteen bytes behind it.
  /// </summary>
  public static UtVideoDecoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);

    if (stream.Width <= 0 || stream.Height <= 0)
      throw new InvalidDataException(
        $"Video stream {stream.Index} states a picture size of {stream.Width}x{stream.Height}, which no frame can be decoded into.");

    var description = stream.CodecPrivateData.Span;
    var extra = description.Length > BitmapInfoHeader.StructSize
      ? description[BitmapInfoHeader.StructSize..]
      : default;

    var format = UtVideoFormat.Parse(stream.Codec, extra, stream.Index);

    if (format.ColourSpace == UtVideoColourSpace.Yuv) {
      if (format.ChromaHorizontalShift > 0 && (stream.Width & 1) != 0)
        throw new InvalidDataException(
          $"Video stream {stream.Index} is {stream.Codec} — subsampled across — but states an odd width of {stream.Width}, so its chrominance planes cover less than the picture.");

      if (format.ChromaVerticalShift > 0 && (stream.Height & 1) != 0)
        throw new InvalidDataException(
          $"Video stream {stream.Index} is {stream.Codec} — subsampled down — but states an odd height of {stream.Height}, so its chrominance planes cover less than the picture.");
    }

    return new(stream.Width, stream.Height, stream.Index, format);
  }

  /// <summary>Decodes one frame, which for this codec is always exactly one whole picture.</summary>
  public bool TryDecode(CodedPacket packet, out RawImage frame) {
    frame = this._Compose(this.DecodePlanes(packet.Data.Span));
    return true;
  }

  /// <summary>
  /// Decodes one frame as far as its planes, before anything is made of their colour.
  /// </summary>
  /// <remarks>
  /// Split out from <see cref="TryDecode"/> so that the planes can be measured as planes. A
  /// subsampled picture's samples do not survive being turned into pixels — the chrominance is
  /// repeated across a block, and comparing the result against another decoder's would be comparing
  /// two conventions rather than two decodings. Every difference between this and ffmpeg is
  /// measured here instead, where a sample is still a sample.
  /// </remarks>
  internal byte[][] DecodePlanes(ReadOnlySpan<byte> data) {
    var format = this._format;
    var predictor = format.PredictorOf(data, this._streamIndex);
    var planes = new byte[format.PlaneCount][];
    var at = 0;

    for (var index = 0; index < format.PlaneCount; ++index) {
      var chroma = format.ColourSpace == UtVideoColourSpace.Yuv && index is 1 or 2;
      var width = chroma ? this._width >> format.ChromaHorizontalShift : this._width;
      var height = chroma ? this._height >> format.ChromaVerticalShift : this._height;
      var verticalShift = chroma ? format.ChromaVerticalShift : 0;

      planes[index] = this._DecodePlane(data, ref at, index, width, height, verticalShift, predictor);
    }

    if (at + format.FrameInfoSize != data.Length)
      throw new InvalidDataException(
        $"A frame of {data.Length} bytes has {format.PlaneCount} planes ending at byte {at}, which with its {format.FrameInfoSize}-byte trailer leaves {data.Length - at - format.FrameInfoSize} bytes unaccounted for.");

    if (format.ColourSpace != UtVideoColourSpace.Yuv)
      _Correlate(planes);

    return planes;
  }

  /// <summary>
  /// Undoes the decorrelation: blue and red are stored as their distance from green.
  /// </summary>
  /// <remarks>
  /// A picture is mostly grey, so blue less green and red less green are small where blue and red
  /// are not, and small numbers are what a Huffman table is good at. It happens after the spatial
  /// prediction has been undone because both are additions and the order between them does not
  /// matter.
  /// <para/>
  /// <b>There is 128 in it as well as green.</b> The community write-up says only that red and blue
  /// are "a difference to the correspondent green value", and a decoder that adds green alone is out
  /// by exactly 128 on every sample of both planes — a picture whose reds and blues are inverted
  /// rather than one that looks broken. Measured on frames where dropping the 128 puts the maximum
  /// difference against ffmpeg at exactly that and keeping it puts it at nought.
  /// <para/>
  /// This belongs with the decoding and not with the colour, because the result is what the plane
  /// holds: an alpha plane is not decorrelated, and neither is green.
  /// </remarks>
  private static void _Correlate(byte[][] planes) {
    var green = planes[0];
    var blue = planes[1];
    var red = planes[2];

    for (var i = 0; i < green.Length; ++i) {
      var g = green[i];
      blue[i] = (byte)(blue[i] + g - 0x80);
      red[i] = (byte)(red[i] + g - 0x80);
    }
  }

  // ============================================================================================
  // A plane
  // ============================================================================================

  /// <summary>
  /// Reads one plane: its code lengths, where each of its slices ends, and then the slices.
  /// </summary>
  /// <remarks>
  /// The end offsets are counted from the start of the plane's data rather than from the start of
  /// each slice, so a slice runs from the previous offset to its own and the last of them is the
  /// length of everything.
  /// </remarks>
  private byte[] _DecodePlane(
    ReadOnlySpan<byte> data, ref int at, int index, int width, int height, int verticalShift,
    UtVideoPredictor predictor) {
    var format = this._format;
    var slices = format.SliceCount;

    if (at + UtVideoHuffmanTable.SYMBOL_COUNT + 4 * slices > data.Length)
      throw new InvalidDataException(
        $"A frame of {data.Length} bytes ends inside the description of plane {index}.");

    var table = new UtVideoHuffmanTable(data.Slice(at, UtVideoHuffmanTable.SYMBOL_COUNT), index);
    at += UtVideoHuffmanTable.SYMBOL_COUNT;

    var ends = new int[slices];
    for (var slice = 0; slice < slices; ++slice) {
      ends[slice] = (int)_ReadUInt32(data, at);
      at += 4;
      if (slice > 0 && ends[slice] < ends[slice - 1])
        throw new InvalidDataException(
          $"Plane {index} states slice {slice} ending at byte {ends[slice]}, before slice {slice - 1} ends at {ends[slice - 1]}.");
    }

    var total = ends[slices - 1];
    if (at + total > data.Length)
      throw new InvalidDataException(
        $"Plane {index} states {total} bytes of slice data where the frame has {data.Length - at} left.");

    var body = data.Slice(at, total);
    at += total;

    var samples = new byte[width * height];
    var start = 0;
    for (var slice = 0; slice < slices; ++slice) {
      var firstRow = format.SliceStart(slice, this._height, verticalShift);
      var lastRow = format.SliceStart(slice + 1, this._height, verticalShift);
      if (firstRow < 0 || lastRow > height || lastRow < firstRow)
        throw new InvalidDataException(
          $"Plane {index} puts slice {slice} at rows {firstRow} to {lastRow} of a plane {height} rows tall.");

      this._DecodeSlice(
        body[start..ends[slice]], table, samples, width, firstRow, lastRow, predictor, index);
      start = ends[slice];
    }

    if (format.SliceStart(slices, this._height, verticalShift) != height)
      throw new InvalidDataException(
        $"Plane {index} is {height} rows tall but its {slices} slices cover "
        + $"{format.SliceStart(slices, this._height, verticalShift)} of them.");

    return samples;
  }

  /// <summary>Reads one slice's symbols and turns them into samples.</summary>
  private void _DecodeSlice(
    ReadOnlySpan<byte> slice, UtVideoHuffmanTable table, byte[] samples, int width, int firstRow,
    int lastRow, UtVideoPredictor predictor, int plane) {
    if (lastRow <= firstRow)
      return;

    var from = firstRow * width;
    var to = lastRow * width;
    var bits = new UtVideoBitReader(slice);
    for (var i = from; i < to; ++i)
      samples[i] = (byte)table.Read(bits);

    switch (predictor) {
      case UtVideoPredictor.None:
        return;
      case UtVideoPredictor.Left:
        UtVideoPrediction.AddLeft(samples.AsSpan(from, to - from), UtVideoPrediction.SLICE_START);
        return;
      case UtVideoPredictor.Median:
      case UtVideoPredictor.Gradient: {
        // The first row of a slice has nothing above it, so it is read from the left whichever of
        // the two the frame states; the rows under it are predicted from their neighbours.
        var firstRowEnd = Math.Min(from + width, to);
        UtVideoPrediction.AddLeft(samples.AsSpan(from, firstRowEnd - from), UtVideoPrediction.SLICE_START);
        UtVideoPrediction.AddPredicted(
          samples, width, firstRowEnd, to, predictor == UtVideoPredictor.Median);
        return;
      }
      default:
        throw new InvalidDataException(
          $"Plane {plane} states prediction method {(int)predictor}, which is none of the four the format has.");
    }
  }

  // ============================================================================================
  // What comes out
  // ============================================================================================

  private RawImage _Compose(byte[][] planes) => this._format.ColourSpace switch {
    UtVideoColourSpace.Rgb => this._FromColour(planes, false),
    UtVideoColourSpace.Rgba => this._FromColour(planes, true),
    _ => this._FromYuv(planes),
  };

  /// <summary>
  /// Puts the colour planes back in order.
  /// </summary>
  /// <remarks>
  /// The planes are green, blue and red in that order, with alpha after them where there is one.
  /// The order is not published correctly anywhere: the community write-up gives it as green, red,
  /// blue, where every file measured here has blue second and red third — which is settled by the
  /// blues and reds of a test pattern coming out the right way round rather than swapped.
  /// </remarks>
  private RawImage _FromColour(byte[][] planes, bool hasAlpha) {
    var count = this._width * this._height;
    var green = planes[0];
    var blue = planes[1];
    var red = planes[2];
    var channels = hasAlpha ? 4 : 3;
    var pixels = new byte[count * channels];
    var alpha = hasAlpha ? planes[3] : null;

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
  /// The conversion is a display convention and not part of the coding, but which of the two
  /// conventions to use is part of it: <c>ULY2</c> and <c>ULH2</c> are the same bits against
  /// different primaries, and the four-character code is the only thing that says which. Both are
  /// studio swing — luminance running 16 to 235 rather than filling the byte — and each chrominance
  /// sample is repeated across the block it covers, which is what a subsampled picture's samples
  /// mean and what the reference decoder's own conversion does.
  /// </remarks>
  private RawImage _FromYuv(byte[][] planes) {
    var luma = planes[0];
    var cb = planes[1];
    var cr = planes[2];
    var format = this._format;
    var chromaWidth = this._width >> format.ChromaHorizontalShift;
    var chromaHeight = this._height >> format.ChromaVerticalShift;
    var pixels = new byte[this._width * this._height * 3];

    // BT.601 and BT.709 at studio swing, scaled by 256.
    var (toRed, toGreenFromBlue, toGreenFromRed, toBlue) = format.IsBt709
      ? (459, -55, -136, 541)
      : (409, -100, -208, 516);

    for (var y = 0; y < this._height; ++y) {
      var chromaRow = Math.Min(y >> format.ChromaVerticalShift, chromaHeight - 1);
      var lumaRow = y * this._width;
      var target = lumaRow * 3;

      for (var x = 0; x < this._width; ++x) {
        var chromaColumn = Math.Min(x >> format.ChromaHorizontalShift, chromaWidth - 1);
        var at = chromaRow * chromaWidth + chromaColumn;

        var scaledLuma = 298 * (luma[lumaRow + x] - 16);
        var blueDifference = cb[at] - 128;
        var redDifference = cr[at] - 128;

        pixels[target] = _Clamp(scaledLuma + toRed * redDifference + 128);
        pixels[target + 1] = _Clamp(scaledLuma + toGreenFromBlue * blueDifference + toGreenFromRed * redDifference + 128);
        pixels[target + 2] = _Clamp(scaledLuma + toBlue * blueDifference + 128);
        target += 3;
      }
    }

    return new() {
      Width = this._width,
      Height = this._height,
      Format = PixelFormat.Rgb24,
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
