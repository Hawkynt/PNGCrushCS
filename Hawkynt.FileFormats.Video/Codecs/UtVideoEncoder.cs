using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.Bmp;
using FileFormat.Codecs.UtVideo;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>
/// Encodes Ut Video: every picture as one key frame of Huffman-coded prediction differences, in
/// the eight-bit colour spaces the decoder reads.
/// </summary>
/// <remarks>
/// Adapted from FFmpeg's <c>libavcodec/utvideoenc.c</c>, copyright (c) 2012 Jan Ekström,
/// LGPL-2.1-or-later; this adaptation is distributed with PNGCrushCS under LGPL-3.0-or-later. The
/// Huffman table construction is in <see cref="UtVideoHuffmanBuilder"/> with its own attribution.
/// <para/>
/// <b>What is written.</b> The stream's <c>strf</c> is a <c>BITMAPINFOHEADER</c> naming the
/// four-character code as its compression, with the sixteen bytes behind it that
/// <see cref="UtVideoFormat"/> describes: the encoder version, the picture's original format, a
/// four-byte frame trailer, and the flags — Huffman coded, progressive, and the slice count less one
/// in the top byte. Every packet is a whole picture: for each plane in turn its 256 code lengths,
/// one cumulative end offset a slice, and the slices' bits; then the trailer stating the predictor.
/// The planes are green, blue less green, red less green and alpha for the colour codes, each
/// difference offset by 128, and luminance, Cb and Cr for the rest. Every one of those conventions
/// is written down at the decoder, where it was measured; this is the same rule run backwards, and
/// the round trip through the decoder is what holds the two together.
/// <para/>
/// <b>What is chosen.</b> The four-character code comes from the stream description where it names
/// one of the eight codes read here, and is <c>ULRG</c> otherwise — the one layout that reproduces
/// any eight-bit colour picture exactly. The predictor defaults to the median, which is what the
/// format's own encoder writes; left and gradient are the same rules with fewer terms and none is
/// the absence of one. The slice count defaults to one for every 120 rows of the subsampled height,
/// which is the reference encoder's rule and what lets its decoder use its threads.
/// <para/>
/// <b>What is accepted.</b> For the colour codes, any picture that turns into eight-bit colour
/// without changing a sample — RGB and BGR with or without alpha, grey, palettised, 5-6-5 — with
/// alpha dropped for <c>ULRG</c>, which has no plane for it. For the luminance codes, the matching
/// planar eight-bit YUV picture is coded sample for sample; an eight-bit colour picture is converted
/// first, under BT.601 for <c>ULY*</c> and BT.709 for <c>ULH*</c> at studio swing, which is the
/// matrix the decoder converts back with. That conversion is the one place a sample changes on its
/// way through, and it happens only where the caller asked for a luminance code and handed over
/// colour. Anything deeper than eight bits, floating-point, or YUV of another subsampling is refused
/// by name rather than quantised or resampled. A picture whose size differs from the stream's is
/// refused too, as is an odd width or height for a code that subsamples across or down it.
/// </remarks>
public sealed class UtVideoEncoder : IVideoCodecEncoder<UtVideoEncoder> {

  /// <summary>The size of the trailer at the end of every frame, which is what the reference encoder writes.</summary>
  private const int _FRAME_INFO_SIZE = 4;

  /// <summary>The most slices a frame can state: the top byte of the flags, plus one.</summary>
  private const int _MAX_SLICES = 256;

  /// <summary>The rows of subsampled height a slice covers when the caller leaves the count open.</summary>
  private const int _ROWS_PER_AUTOMATIC_SLICE = 120;

  private static readonly CodecTag _DefaultTag = CodecTag.FromCharacters("ULRG");

  /// <summary>The codes this encoder writes.</summary>
  private static readonly CodecTag[] _Tags = [
    _DefaultTag,
    CodecTag.FromCharacters("ULRA"),
    CodecTag.FromCharacters("ULY0"),
    CodecTag.FromCharacters("ULY2"),
    CodecTag.FromCharacters("ULY4"),
    CodecTag.FromCharacters("ULH0"),
    CodecTag.FromCharacters("ULH2"),
    CodecTag.FromCharacters("ULH4"),
  ];

  private readonly MediaStreamInfo _stream;
  private readonly UtVideoFormat _format;
  private readonly UtVideoPredictor _predictor;
  private readonly int _width;
  private readonly int _height;

  /// <summary>The format a picture is coded from, which is what every input is brought to first.</summary>
  private readonly PixelFormat _coded;

  /// <summary>How colour is turned into luminance where a luminance code is asked for colour.</summary>
  private readonly RawImageColorInfo? _conversion;

  private UtVideoEncoder(MediaStreamInfo stream, CodecTag tag, UtVideoFormat format, UtVideoPredictor predictor) {
    this._format = format;
    this._predictor = predictor;
    this._width = stream.Width;
    this._height = stream.Height;

    (this._coded, this._conversion) = format.ColourSpace switch {
      UtVideoColourSpace.Rgb => (PixelFormat.Rgb24, null),
      UtVideoColourSpace.Rgba => (PixelFormat.Rgba32, null),
      _ => (
        format.ChromaVerticalShift > 0 ? PixelFormat.Yuv420P8
        : format.ChromaHorizontalShift > 0 ? PixelFormat.Yuv422P8
        : PixelFormat.Yuv444P8,
        format.IsBt709 ? RawImageColorInfo.Bt709Limited : RawImageColorInfo.Bt601Limited),
    };

    this._stream = new() {
      Index = stream.Index,
      Kind = MediaStreamKind.Video,
      Codec = tag,
      Handler = tag,
      TimeBase = stream.TimeBase,
      FrameRate = stream.FrameRate,
      DeclaredFrameCount = stream.DeclaredFrameCount,
      Width = stream.Width,
      Height = stream.Height,
      BitsPerPixel = format.HasAlpha ? 32 : 24,
      CodecPrivateData = _PrivateData(stream.Width, stream.Height, tag, format),
      Language = stream.Language,
      Name = stream.Name,
    };
  }

  public static string CodecName => "Ut Video";

  /// <summary>
  /// The code the default layout is named by. The layout actually written is whichever
  /// <see cref="DescribeStream"/> names, which follows the stream the encoder was created for.
  /// </summary>
  public static CodecTag Codec => _DefaultTag;

  /// <summary>Builds an encoder with the median predictor and the automatic slice count.</summary>
  public static UtVideoEncoder Create(MediaStreamInfo stream) => Create(stream, UtVideoPredictor.Median);

  /// <summary>
  /// Builds an encoder with a chosen predictor and slice count.
  /// </summary>
  /// <param name="stream">The stream to write, whose code picks the layout where it is one of the eight.</param>
  /// <param name="predictor">How each sample is predicted from the ones before it.</param>
  /// <param name="sliceCount">
  /// How many bands a frame is cut into, from one to 256 and no more than the subsampled height, or
  /// nought for one band every 120 rows.
  /// </param>
  public static UtVideoEncoder Create(MediaStreamInfo stream, UtVideoPredictor predictor, int sliceCount = 0) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.Kind != MediaStreamKind.Video)
      throw new NotSupportedException("Ut Video can only encode a video stream.");
    if (stream.Width <= 0 || stream.Height <= 0)
      throw new NotSupportedException(
        $"A Ut Video encoder needs positive picture dimensions before the muxer is created; {stream.Width}x{stream.Height} was supplied.");
    if (!Enum.IsDefined(predictor))
      throw new ArgumentOutOfRangeException(nameof(predictor), predictor, "Ut Video has four predictors: none, left, gradient and median.");
    if (sliceCount < 0 || sliceCount > _MAX_SLICES)
      throw new ArgumentOutOfRangeException(nameof(sliceCount), sliceCount, $"A Ut Video frame has between 1 and {_MAX_SLICES} slices, or nought to let the encoder choose.");

    var tag = _TagOf(stream);
    var layout = UtVideoFormat.ForEncoding(tag, 1, _FRAME_INFO_SIZE, stream.Index);

    if (layout.ChromaHorizontalShift > 0 && (stream.Width & 1) != 0)
      throw new NotSupportedException(
        $"{tag} subsamples the chrominance across, so its width must be even; {stream.Width} is not. Use ULY4 or a colour code for this picture.");
    if (layout.ChromaVerticalShift > 0 && (stream.Height & 1) != 0)
      throw new NotSupportedException(
        $"{tag} subsamples the chrominance down, so its height must be even; {stream.Height} is not. Use ULY2, ULY4 or a colour code for this picture.");

    var subsampledHeight = stream.Height >> layout.ChromaVerticalShift;
    if (sliceCount == 0)
      sliceCount = Math.Clamp(subsampledHeight / _ROWS_PER_AUTOMATIC_SLICE, 1, _MAX_SLICES);
    else if (sliceCount > subsampledHeight)
      throw new NotSupportedException(
        $"{sliceCount} slices were asked for, but a {tag} frame {stream.Height} rows tall has only {subsampledHeight} rows of chrominance to cut between.");

    return new(stream, tag, UtVideoFormat.ForEncoding(tag, sliceCount, _FRAME_INFO_SIZE, stream.Index), predictor);
  }

  public bool TryEncode(RawImage frame, long? presentationTimestamp, out CodedPacket packet) {
    ArgumentNullException.ThrowIfNull(frame);
    if (frame.Width != this._width || frame.Height != this._height)
      throw new InvalidDataException(
        $"Ut Video geometry is fixed at {this._width}x{this._height} for the life of the stream; received {frame.Width}x{frame.Height}.");
    if (!frame.HasEnoughPixelData)
      throw new InvalidDataException("The source RawImage does not contain enough pixel data for its declared format and dimensions.");

    var planes = this._Planes(frame);
    var format = this._format;

    using var output = new MemoryStream();
    for (var index = 0; index < planes.Length; ++index) {
      var chroma = format.ColourSpace == UtVideoColourSpace.Yuv && index is 1 or 2;
      var width = chroma ? this._width >> format.ChromaHorizontalShift : this._width;
      var verticalShift = chroma ? format.ChromaVerticalShift : 0;
      this._EncodePlane(output, planes[index], width, verticalShift);
    }

    Span<byte> trailer = stackalloc byte[_FRAME_INFO_SIZE];
    BinaryPrimitives.WriteUInt32LittleEndian(trailer, (uint)this._predictor << 8);
    output.Write(trailer);

    packet = new(
      this._stream.Index,
      output.ToArray(),
      PresentationTimestamp: presentationTimestamp,
      DecodeTimestamp: presentationTimestamp,
      IsKeyFrame: true);
    return true;
  }

  public MediaStreamInfo DescribeStream() => this._stream;

  // ============================================================================================
  // What goes in
  // ============================================================================================

  /// <summary>The code to write, from the stream's own where it is one this writes.</summary>
  private static CodecTag _TagOf(MediaStreamInfo stream) {
    foreach (var tag in _Tags)
      if (stream.Codec.EqualsIgnoringCase(tag))
        return tag;

    var name = stream.Codec.ToString();
    var family = name.ToUpperInvariant();
    if (family.StartsWith("UQ", StringComparison.Ordinal))
      throw new NotSupportedException(
        $"Stream {stream.Index} asks for {name}, one of the ten-bit Ut Video Pro codes, whose bitstream is not published. The eight-bit codes are written; use ULRG, ULRA, ULY0, ULY2, ULY4 or their ULH* spellings.");
    if (family.StartsWith("UM", StringComparison.Ordinal))
      throw new NotSupportedException(
        $"Stream {stream.Index} asks for {name}, one of the Ut Video T2 codes, which is a different codec whose bitstream is not published. The eight-bit codes are written; use ULRG, ULRA, ULY0, ULY2, ULY4 or their ULH* spellings.");

    return _DefaultTag;
  }

  /// <summary>
  /// The planes to code, in the order the format writes them, each exactly the size of its plane.
  /// </summary>
  private byte[][] _Planes(RawImage frame) {
    var picture = this._Prepared(frame);
    var count = this._width * this._height;

    if (this._format.ColourSpace == UtVideoColourSpace.Yuv) {
      var planes = new byte[3][];
      for (var index = 0; index < 3; ++index) {
        var (width, height) = picture.GetPlaneDimensions(index);
        var expectedWidth = index == 0 ? this._width : this._width >> this._format.ChromaHorizontalShift;
        var expectedHeight = index == 0 ? this._height : this._height >> this._format.ChromaVerticalShift;
        if (width != expectedWidth || height != expectedHeight)
          throw new InvalidDataException(
            $"Plane {index} of the {picture.Format} picture is {width}x{height} where {this._stream.Codec} codes {expectedWidth}x{expectedHeight}.");

        planes[index] = picture.GetPlaneData(index).ToArray();
      }

      return planes;
    }

    // Green as it is, blue and red as their distance from green offset by 128, alpha as it is.
    var channels = this._format.HasAlpha ? 4 : 3;
    var pixels = picture.PixelData;
    var green = new byte[count];
    var blue = new byte[count];
    var red = new byte[count];
    var alpha = this._format.HasAlpha ? new byte[count] : null;

    for (var i = 0; i < count; ++i) {
      var at = i * channels;
      var g = pixels[at + 1];
      green[i] = g;
      blue[i] = (byte)(pixels[at + 2] - g + 0x80);
      red[i] = (byte)(pixels[at] - g + 0x80);
      if (alpha != null)
        alpha[i] = pixels[at + 3];
    }

    return alpha == null ? [green, blue, red] : [green, blue, red, alpha];
  }

  /// <summary>Brings the picture to the coded format, refusing by name where that would change a sample it should not.</summary>
  private RawImage _Prepared(RawImage frame) {
    if (frame.Format == this._coded)
      return frame;

    if (!_IsEightBitColour(frame.Format))
      throw new NotSupportedException(
        this._conversion == null
          ? $"{this._stream.Codec} is lossless and codes eight-bit colour; a {frame.Format} picture cannot be converted to it without changing sample values, so it is refused rather than quantised."
          : $"{this._stream.Codec} codes {this._coded} sample for sample, or eight-bit colour converted to it; a {frame.Format} picture would have to be resampled or quantised on the way, so it is refused.");

    return FastRawImageConverter.Convert(frame, this._coded, this._conversion);
  }

  private static bool _IsEightBitColour(PixelFormat format) => format is
    PixelFormat.Bgr24 or PixelFormat.Rgb24
    or PixelFormat.Bgra32 or PixelFormat.Rgba32 or PixelFormat.Argb32
    or PixelFormat.Gray8 or PixelFormat.GrayAlpha16
    or PixelFormat.Indexed8 or PixelFormat.Indexed4 or PixelFormat.Indexed1 or PixelFormat.Indexed16
    or PixelFormat.Rgb565;

  // ============================================================================================
  // A plane
  // ============================================================================================

  /// <summary>
  /// Writes one plane: its code lengths, where each of its slices ends, and then the slices.
  /// </summary>
  /// <remarks>
  /// The prediction runs a slice at a time and starts over at every slice, which is what makes a
  /// slice decodable on its own; the table is one for the whole plane, built from the differences
  /// of every slice together.
  /// </remarks>
  private void _EncodePlane(MemoryStream output, byte[] samples, int width, int verticalShift) {
    var format = this._format;
    var slices = format.SliceCount;
    var differences = new byte[samples.Length];
    var bounds = new int[slices + 1];

    for (var slice = 0; slice <= slices; ++slice)
      bounds[slice] = format.SliceStart(slice, this._height, verticalShift) * width;

    for (var slice = 0; slice < slices; ++slice)
      this._Predict(samples, differences, width, bounds[slice], bounds[slice + 1]);

    Span<long> counts = stackalloc long[UtVideoHuffmanTable.SYMBOL_COUNT];
    foreach (var difference in differences)
      ++counts[difference];

    var lengths = UtVideoHuffmanBuilder.Lengths(counts);
    output.Write(lengths);

    // One symbol and no other: the slices carry nothing and every end offset is nought.
    var single = Array.IndexOf(lengths, (byte)0);
    if (single >= 0) {
      output.Write(new byte[4 * slices]);
      return;
    }

    var codes = UtVideoHuffmanBuilder.Codes(lengths);
    var bodies = new byte[slices][];
    Span<byte> end = stackalloc byte[4];
    var total = 0;
    for (var slice = 0; slice < slices; ++slice) {
      var from = bounds[slice];
      var to = bounds[slice + 1];
      var bits = new UtVideoBitWriter(to - from);
      for (var i = from; i < to; ++i) {
        var symbol = differences[i];
        bits.Write(codes[symbol], lengths[symbol]);
      }

      bodies[slice] = bits.End();
      total += bodies[slice].Length;
      BinaryPrimitives.WriteUInt32LittleEndian(end, (uint)total);
      output.Write(end);
    }

    foreach (var body in bodies)
      output.Write(body);
  }

  /// <summary>
  /// Turns one slice's samples into the differences the decoder adds back, under the predictor the
  /// frame states.
  /// </summary>
  /// <remarks>
  /// The mirror of <see cref="UtVideoPrediction"/>, rule for rule: the slice is one run of samples
  /// with nothing reset at the end of a row; the first sample is predicted from 128; the first row
  /// from the left alone; the first sample of the second row from the one above it; and after that
  /// the median takes its left neighbour from the sample before it in the run wherever it is,
  /// while the gradient starts every row from the sample above.
  /// </remarks>
  private void _Predict(byte[] samples, byte[] differences, int width, int from, int to) {
    if (from >= to)
      return;

    if (this._predictor == UtVideoPredictor.None) {
      Array.Copy(samples, from, differences, from, to - from);
      return;
    }

    var firstRowEnd = this._predictor == UtVideoPredictor.Left ? to : Math.Min(from + width, to);
    var previous = UtVideoPrediction.SLICE_START;
    for (var i = from; i < firstRowEnd; ++i) {
      differences[i] = (byte)(samples[i] - previous);
      previous = samples[i];
    }

    if (firstRowEnd >= to)
      return;

    var median = this._predictor == UtVideoPredictor.Median;
    differences[firstRowEnd] = (byte)(samples[firstRowEnd] - samples[firstRowEnd - width]);

    for (var i = firstRowEnd + 1; i < to; ++i) {
      var above = samples[i - width];
      byte predicted;

      if (!median && i % width == 0) {
        predicted = above;
      } else {
        var left = samples[i - 1];
        var aboveLeft = samples[i - width - 1];
        var gradient = (byte)(left + above - aboveLeft);
        predicted = median ? _Median(left, above, gradient) : gradient;
      }

      differences[i] = (byte)(samples[i] - predicted);
    }
  }

  private static byte _Median(byte a, byte b, byte c) {
    if (a > b)
      (a, b) = (b, a);

    return c < a ? a : c > b ? b : c;
  }

  // ============================================================================================
  // The description
  // ============================================================================================

  /// <summary>
  /// A <c>BITMAPINFOHEADER</c> naming the code, with the format's sixteen bytes behind it.
  /// </summary>
  /// <remarks>
  /// The header's own size field counts the sixteen bytes, which is how the reference muxer writes
  /// it and how its demuxer knows where the codec's description ends.
  /// </remarks>
  private static byte[] _PrivateData(int width, int height, CodecTag tag, UtVideoFormat format) {
    var extra = format.Describe();
    var data = new byte[BitmapInfoHeader.StructSize + extra.Length];
    var span = data.AsSpan();
    var bitsPerPixel = format.HasAlpha ? 32 : 24;

    BinaryPrimitives.WriteInt32LittleEndian(span, data.Length);
    BinaryPrimitives.WriteInt32LittleEndian(span[4..], width);
    BinaryPrimitives.WriteInt32LittleEndian(span[8..], height);
    BinaryPrimitives.WriteInt16LittleEndian(span[12..], 1);
    BinaryPrimitives.WriteInt16LittleEndian(span[14..], (short)bitsPerPixel);
    BinaryPrimitives.WriteUInt32LittleEndian(span[16..], tag.Value);
    BinaryPrimitives.WriteInt32LittleEndian(span[20..], checked(width * height * (bitsPerPixel / 8)));
    extra.CopyTo(span[BitmapInfoHeader.StructSize..]);
    return data;
  }
}
