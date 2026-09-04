using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.Codecs.Ffv1;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>
/// Encodes FFV1, the lossless intra-frame codec of RFC 9043, as version 3 with the range coder.
/// </summary>
/// <remarks>
/// Adapted from FFmpeg's <c>libavcodec/ffv1enc.c</c>, copyright (c) 2003-2013 Michael Niedermayer,
/// LGPL-2.1-or-later; this adaptation is distributed with PNGCrushCS under LGPL-3.0-or-later.
/// <para/>
/// <b>What it writes.</b> One configuration record for the container, and one frame per picture:
/// version 3 with micro-version 4, the range coder with its default state transition table, eight
/// bits a sample, the small context model — three quantisers of eleven levels, 666 contexts — one
/// quantisation table set, a checksum on every slice, and every frame a keyframe with the context
/// model reset, so a stream can be entered anywhere and a damaged frame takes nothing else with it.
/// That is what <c>ffmpeg -c:v ffv1 -level 3 -coder 1 -context 0 -slicecrc 1 -g 1</c> writes, and
/// ffmpeg reads what this writes.
/// <para/>
/// <b>What goes in.</b> The coded format is fixed when the encoder is built, because the container
/// needs the record before the first picture arrives. Grey and grey with alpha are coded as one and
/// two planes; the planar 4:2:0, 4:2:2, 4:4:0 and 4:4:4 formats as luminance and chrominance with
/// the same subsampling, sample for sample; packed colour as the JPEG 2000 reversible transform, with
/// alpha as a fourth plane where the format has one. A picture is converted on the way in only
/// where nothing is lost — a channel order, an opaque alpha added, grey widened to colour — and
/// refused by name otherwise. A lossless codec that quietly subsampled its input would not be one.
/// <para/>
/// <b>Slices.</b> The grid is chosen the way ffmpeg chooses it when nothing is asked for — the
/// first grid of two or more rows whose slices are no larger than 360 by 288 — or stated outright
/// through <see cref="Create(MediaStreamInfo, PixelFormat, int, int)"/>. A grid is refused when a
/// slice would be narrower than a pixel or, with subsampled chrominance and an odd picture width or
/// height, when the slices' chrominance blocks would leave a column or row that no slice codes: a
/// version 3 reader has no way to recover those samples, and ffmpeg refuses the same grids.
/// <para/>
/// <b>What is left.</b> The Golomb-Rice coder, versions 0 and 1, samples deeper than eight bits, a
/// stream's own state transition table, the large context model, and carrying the context model
/// across frames. Each is a smaller stream or an older reader, not a different picture.
/// </remarks>
public sealed class Ffv1Encoder : IVideoCodecEncoder<Ffv1Encoder> {

  private static readonly CodecTag _FFV1 = CodecTag.FromCharacters("FFV1");
  private const string _MATROSKA_CODEC_ID = "V_FFV1";

  private const int _VERSION = 3;
  private const int _MICRO_VERSION = 4;
  private const int _CODER_TYPE_RANGE = 1;
  private const int _COLOUR_SPACE_YCBCR = 0;
  private const int _COLOUR_SPACE_RGB = 1;
  private const int _BITS_PER_RAW_SAMPLE = 8;
  private const int _PICTURE_STRUCTURE_PROGRESSIVE = 3;
  private const int _MAX_SLICES_PER_AXIS = 256;
  private const int _LARGEST_DEFAULT_SLICE = 360 * 288;

  /// <summary>The first half of ffmpeg's eleven-level quantiser, as the lengths of its runs of equal entries.</summary>
  private static ReadOnlySpan<int> _ELEVEN_LEVEL_RUNS => [1, 1, 3, 7, 23, 93];

  /// <summary>A quantiser that puts everything in one level, which is how a context input is switched off.</summary>
  private static ReadOnlySpan<int> _ONE_LEVEL_RUNS => [128];

  private readonly MediaStreamInfo _stream;
  private readonly PixelFormat _format;
  private readonly Ffv1Parameters _parameters;
  private readonly byte[] _zeroState;
  private readonly byte[] _oneState;

  private Ffv1Encoder(MediaStreamInfo stream, PixelFormat format, int horizontalSlices, int verticalSlices) {
    var (colourSpace, chromaPlanes, horizontalShift, verticalShift, extraPlane) = _Layout(format);
    _RefuseGrid(stream.Width, stream.Height, horizontalSlices, verticalSlices, chromaPlanes && colourSpace == _COLOUR_SPACE_YCBCR, horizontalShift, verticalShift);

    var record = _WriteConfigurationRecord(colourSpace, chromaPlanes, horizontalShift, verticalShift, extraPlane, horizontalSlices, verticalSlices);
    (this._zeroState, this._oneState) = Ffv1StateTransition.Build([]);

    // Read back through the decoder's own parser rather than kept from what was written: the
    // expanded quantisation tables and the context counts come out of that, and so does the proof
    // that the record describes what it was meant to.
    var states = _FreshStates();
    this._parameters = Ffv1Parameters.Read(new Ffv1RangeCoder(record.AsMemory(..^4), this._zeroState, this._oneState), states, true);
    this._format = format;
    this._stream = new() {
      Index = stream.Index,
      Kind = MediaStreamKind.Video,
      Codec = _FFV1,
      Handler = _FFV1,
      CodecId = _MATROSKA_CODEC_ID,
      TimeBase = stream.TimeBase,
      FrameRate = stream.FrameRate,
      DeclaredFrameCount = stream.DeclaredFrameCount,
      Width = stream.Width,
      Height = stream.Height,
      BitsPerPixel = RawImage.BitsPerPixel(format),
      CodecPrivateData = record,
      Language = stream.Language,
      Name = stream.Name,
    };
  }

  public static string CodecName => "FFV1 (RFC 9043, version 3, range coder)";

  public static CodecTag Codec => _FFV1;

  /// <summary>
  /// Builds an encoder, taking the coded format from the bits per pixel the stream states.
  /// </summary>
  /// <remarks>
  /// Eight bits is grey, twelve is 4:2:0, sixteen is 4:2:2, twenty-four is colour and thirty-two is
  /// colour with alpha. A stream that states nothing is coded as colour, which is what every decoder
  /// here hands back and the only choice that loses nothing whatever arrives. Anything else — 4:4:4,
  /// 4:4:0, grey with alpha, a slice grid of one's own — is asked for by name through
  /// <see cref="Create(MediaStreamInfo, PixelFormat, int, int)"/>.
  /// </remarks>
  public static Ffv1Encoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);

    var format = stream.BitsPerPixel switch {
      0 => PixelFormat.Rgb24,
      8 => PixelFormat.Gray8,
      12 => PixelFormat.Yuv420P8,
      16 => PixelFormat.Yuv422P8,
      24 => PixelFormat.Rgb24,
      32 => PixelFormat.Rgba32,
      _ => throw new NotSupportedException(
        $"Video stream {stream.Index} states {stream.BitsPerPixel} bits per pixel, which names no eight-bit format FFV1 is written in here. Name the format outright instead."),
    };

    return Create(stream, format, 0, 0);
  }

  /// <summary>
  /// Builds an encoder for one coded format and, where asked, one slice grid.
  /// </summary>
  /// <param name="stream">The stream to describe: index, size, time base and the rest are carried over.</param>
  /// <param name="format">
  /// What the samples are coded as: <see cref="PixelFormat.Gray8"/>, <see cref="PixelFormat.GrayAlpha16"/>,
  /// one of the eight-bit planar formats, <see cref="PixelFormat.Rgb24"/> or <see cref="PixelFormat.Rgba32"/>.
  /// <see cref="PixelFormat.Bgr24"/>, <see cref="PixelFormat.Bgra32"/> and <see cref="PixelFormat.Argb32"/>
  /// are taken as the same thing in another order.
  /// </param>
  /// <param name="horizontalSlices">How many columns of slices, or nought to choose as ffmpeg would.</param>
  /// <param name="verticalSlices">How many rows of slices, or nought to choose as ffmpeg would.</param>
  public static Ffv1Encoder Create(MediaStreamInfo stream, PixelFormat format, int horizontalSlices = 0, int verticalSlices = 0) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.Kind != MediaStreamKind.Video)
      throw new NotSupportedException($"FFV1 codes video, and stream {stream.Index} is {stream.Kind}.");

    if (stream.Width <= 0 || stream.Height <= 0)
      throw new NotSupportedException(
        $"Video stream {stream.Index} states a picture size of {stream.Width}x{stream.Height}, and FFV1 needs the size before the first picture to describe the stream.");

    var coded = _CodedFormat(format);
    var (colourSpace, chromaPlanes, horizontalShift, verticalShift, _) = _Layout(coded);
    var subsampled = chromaPlanes && colourSpace == _COLOUR_SPACE_YCBCR;

    if ((horizontalSlices <= 0) != (verticalSlices <= 0))
      throw new ArgumentException("A slice grid is stated as both its width and its height, or as neither.", nameof(horizontalSlices));

    if (horizontalSlices <= 0)
      (horizontalSlices, verticalSlices) = _DefaultGrid(stream.Width, stream.Height, subsampled, horizontalShift, verticalShift);

    return new(stream, coded, horizontalSlices, verticalSlices);
  }

  /// <summary>
  /// Turns one picture into one keyframe.
  /// </summary>
  /// <remarks>
  /// Every packet is a whole frame and a keyframe: the context model starts from its initial states
  /// in every slice of every frame, so nothing about a frame depends on the one before it and the
  /// presentation timestamp is the decoding timestamp.
  /// </remarks>
  public bool TryEncode(RawImage frame, long? presentationTimestamp, out CodedPacket packet) {
    ArgumentNullException.ThrowIfNull(frame);
    if (frame.Width != this._stream.Width || frame.Height != this._stream.Height)
      throw new InvalidDataException(
        $"The encoder describes {this._stream.Width}x{this._stream.Height} pictures, and this one is {frame.Width}x{frame.Height}.");

    if (!frame.HasEnoughPixelData)
      throw new InvalidDataException(
        $"A {frame.Width}x{frame.Height} {frame.Format} picture needs at least {frame.MinimumPixelDataLength} bytes of samples and this one carries {frame.PixelData?.Length ?? 0}.");

    var source = this._TakeLosslessly(frame);
    var planes = this._PlanesOf(source);
    var data = this._EncodeFrame(planes);

    packet = new(
      StreamIndex: this._stream.Index,
      Data: data,
      PresentationTimestamp: presentationTimestamp,
      DecodeTimestamp: presentationTimestamp,
      IsKeyFrame: true);
    return true;
  }

  public MediaStreamInfo DescribeStream() => this._stream;

  // ============================================================================================
  // The configuration record
  // ============================================================================================

  /// <summary>Writes the record a version 3 container carries (RFC 9043 §4.2), checksum included.</summary>
  private static byte[] _WriteConfigurationRecord(
    int colourSpace, bool chromaPlanes, int horizontalShift, int verticalShift, bool extraPlane, int horizontalSlices, int verticalSlices) {
    var (zero, one) = Ffv1StateTransition.Build([]);
    var coder = new Ffv1RangeEncoder(zero, one);
    var states = _FreshStates();

    coder.Symbol(states, _VERSION, false);
    coder.Symbol(states, _MICRO_VERSION, false);
    coder.Symbol(states, _CODER_TYPE_RANGE, false);
    coder.Symbol(states, colourSpace, false);
    coder.Symbol(states, _BITS_PER_RAW_SAMPLE, false);
    coder.Put(states, 0, chromaPlanes ? 1 : 0);
    coder.Symbol(states, horizontalShift, false);
    coder.Symbol(states, verticalShift, false);
    coder.Put(states, 0, extraPlane ? 1 : 0);
    coder.Symbol(states, horizontalSlices - 1, false);
    coder.Symbol(states, verticalSlices - 1, false);

    coder.Symbol(states, 1, false);   // one quantisation table set
    _WriteQuantTable(coder, _ELEVEN_LEVEL_RUNS);
    _WriteQuantTable(coder, _ELEVEN_LEVEL_RUNS);
    _WriteQuantTable(coder, _ELEVEN_LEVEL_RUNS);
    _WriteQuantTable(coder, _ONE_LEVEL_RUNS);
    _WriteQuantTable(coder, _ONE_LEVEL_RUNS);

    coder.Put(states, 0, 0);          // no initial states of its own
    coder.Symbol(states, 1, false);   // a checksum on every slice
    coder.Symbol(states, 1, false);   // every frame a keyframe

    var body = coder.Terminate(false);
    var record = new byte[body.Length + 4];
    body.CopyTo(record, 0);
    BinaryPrimitives.WriteUInt32BigEndian(record.AsSpan(body.Length), Ffv1Crc.Of(body));
    return record;
  }

  /// <summary>
  /// Writes one quantiser as the lengths of its runs, each one less than it is (RFC 9043 §4.1).
  /// </summary>
  /// <remarks>
  /// Only the first half is written; the reader mirrors it. Each table starts from states of its own,
  /// which is how the reader reads it.
  /// </remarks>
  private static void _WriteQuantTable(Ffv1RangeEncoder coder, ReadOnlySpan<int> runs) {
    var states = _FreshStates();
    foreach (var run in runs)
      coder.Symbol(states, run - 1, false);
  }

  // ============================================================================================
  // The frame
  // ============================================================================================

  private byte[] _EncodeFrame(Ffv1Plane[] planes) {
    var parameters = this._parameters;
    var output = new MemoryStream();
    var encoder = new Ffv1SliceEncoder(parameters);

    // The keyframe bit is coded by the first slice's coder, against a state of its own that starts
    // at 128 for every frame, because the first slice begins at the same byte the frame does.
    var frameCoder = new Ffv1RangeEncoder(this._zeroState, this._oneState);
    var keyframeState = _FreshStates();
    frameCoder.Put(keyframeState, 0, 1);

    for (var sliceY = 0; sliceY < parameters.VerticalSlices; ++sliceY)
      for (var sliceX = 0; sliceX < parameters.HorizontalSlices; ++sliceX) {
        var first = sliceX == 0 && sliceY == 0;
        var coder = first ? frameCoder : new Ffv1RangeEncoder(this._zeroState, this._oneState);

        this._EncodeSlice(encoder, coder, planes, sliceX, sliceY);
        _AppendSlice(output, coder.Terminate(true));
      }

    return output.ToArray();
  }

  /// <summary>Codes one slice: its header (RFC 9043 §4.5), then its samples.</summary>
  private void _EncodeSlice(Ffv1SliceEncoder encoder, Ffv1RangeEncoder coder, Ffv1Plane[] planes, int sliceX, int sliceY) {
    var parameters = this._parameters;
    var headerStates = _FreshStates();

    coder.Symbol(headerStates, sliceX, false);
    coder.Symbol(headerStates, sliceY, false);
    coder.Symbol(headerStates, 0, false);   // one column wide
    coder.Symbol(headerStates, 0, false);   // one row high
    for (var i = 0; i < parameters.QuantTableSetIndexCount; ++i)
      coder.Symbol(headerStates, 0, false);

    coder.Symbol(headerStates, _PICTURE_STRUCTURE_PROGRESSIVE, false);
    coder.Symbol(headerStates, 0, false);   // sample aspect ratio unknown, as ffmpeg writes it
    coder.Symbol(headerStates, 1, false);

    var x = (int)((long)sliceX * this._stream.Width / parameters.HorizontalSlices);
    var y = (int)((long)sliceY * this._stream.Height / parameters.VerticalSlices);
    var width = (int)((long)(sliceX + 1) * this._stream.Width / parameters.HorizontalSlices) - x;
    var height = (int)((long)(sliceY + 1) * this._stream.Height / parameters.VerticalSlices) - y;

    var slicePlanes = new Ffv1Plane[planes.Length];
    for (var plane = 0; plane < planes.Length; ++plane)
      slicePlanes[plane] = this._CutOut(planes[plane], plane, x, y, width, height);

    var states = this._FreshContexts();

    if (parameters.ColourSpaceType == _COLOUR_SPACE_YCBCR) {
      for (var plane = 0; plane < slicePlanes.Length; ++plane)
        encoder.EncodePlane(coder, slicePlanes[plane], states[parameters.PlaneKindOf(plane)], 0);
    } else {
      for (var line = 0; line < height; ++line)
        for (var plane = 0; plane < slicePlanes.Length; ++plane)
          encoder.EncodeLine(coder, slicePlanes[plane], line, states[parameters.PlaneKindOf(plane)], 0);
    }
  }

  /// <summary>
  /// Appends a slice with its footer: the length in three bytes, a nought, and the checksum
  /// (RFC 9043 §4.8).
  /// </summary>
  /// <remarks>
  /// The checksum covers the slice, the length and the nought, and is the value that makes the
  /// whole footer come out at nothing when a reader runs the check over all of it.
  /// </remarks>
  private static void _AppendSlice(MemoryStream output, byte[] slice) {
    var footer = new byte[slice.Length + 8];
    slice.CopyTo(footer, 0);
    footer[slice.Length] = (byte)(slice.Length >> 16);
    footer[slice.Length + 1] = (byte)(slice.Length >> 8);
    footer[slice.Length + 2] = (byte)slice.Length;
    footer[slice.Length + 3] = 0;
    BinaryPrimitives.WriteUInt32BigEndian(footer.AsSpan(slice.Length + 4), Ffv1Crc.Of(footer.AsSpan(0, slice.Length + 4)));
    output.Write(footer);
  }

  private Ffv1Plane _CutOut(Ffv1Plane source, int plane, int x, int y, int width, int height) {
    var parameters = this._parameters;
    var subsampled = parameters.ColourSpaceType == _COLOUR_SPACE_YCBCR && parameters.ChromaPlanes && plane is 1 or 2;
    var horizontal = subsampled ? parameters.ChromaHorizontalShift : 0;
    var vertical = subsampled ? parameters.ChromaVerticalShift : 0;

    var x0 = x >> horizontal;
    var y0 = y >> vertical;
    var cut = new Ffv1Plane((width + (1 << horizontal) - 1) >> horizontal, (height + (1 << vertical) - 1) >> vertical);
    for (var row = 0; row < cut.Height; ++row)
      for (var column = 0; column < cut.Width; ++column)
        cut[column, row] = source[x0 + column, y0 + row];

    return cut;
  }

  /// <summary>The initial states of every context of every kind of plane, which every slice of every frame starts from.</summary>
  private byte[][][] _FreshContexts() {
    var parameters = this._parameters;
    var kinds = new byte[3][][];
    for (var plane = 0; plane < parameters.PlaneCount; ++plane) {
      var kind = parameters.PlaneKindOf(plane);
      if (kinds[kind] != null)
        continue;

      var contexts = new byte[parameters.ContextCount[0]][];
      for (var context = 0; context < contexts.Length; ++context)
        contexts[context] = _FreshStates();

      kinds[kind] = contexts;
    }

    return kinds;
  }

  private static byte[] _FreshStates() {
    var states = new byte[Ffv1RangeCoder.CONTEXT_SIZE];
    Array.Fill(states, (byte)128);
    return states;
  }

  // ============================================================================================
  // What goes in
  // ============================================================================================

  private static (int ColourSpace, bool ChromaPlanes, int HorizontalShift, int VerticalShift, bool ExtraPlane) _Layout(PixelFormat format) => format switch {
    PixelFormat.Gray8 => (_COLOUR_SPACE_YCBCR, false, 0, 0, false),
    PixelFormat.GrayAlpha16 => (_COLOUR_SPACE_YCBCR, false, 0, 0, true),
    PixelFormat.Yuv420P8 => (_COLOUR_SPACE_YCBCR, true, 1, 1, false),
    PixelFormat.Yuv422P8 => (_COLOUR_SPACE_YCBCR, true, 1, 0, false),
    PixelFormat.Yuv440P8 => (_COLOUR_SPACE_YCBCR, true, 0, 1, false),
    PixelFormat.Yuv444P8 => (_COLOUR_SPACE_YCBCR, true, 0, 0, false),
    PixelFormat.Rgb24 => (_COLOUR_SPACE_RGB, true, 0, 0, false),
    PixelFormat.Rgba32 => (_COLOUR_SPACE_RGB, true, 0, 0, true),
    _ => throw new NotSupportedException(
      $"{format} is not a format FFV1 is written in here. Eight-bit grey, grey with alpha, planar 4:2:0, 4:2:2, 4:4:0 and 4:4:4, and packed colour with or without alpha are."),
  };

  /// <summary>The format a picture is coded as, which for a packed format is its canonical channel order.</summary>
  private static PixelFormat _CodedFormat(PixelFormat format) {
    var coded = format switch {
      PixelFormat.Bgr24 => PixelFormat.Rgb24,
      PixelFormat.Bgra32 or PixelFormat.Argb32 => PixelFormat.Rgba32,
      _ => format,
    };

    _ = _Layout(coded);
    return coded;
  }

  /// <summary>
  /// Brings a picture into the coded format, where that loses nothing, and refuses it where it would.
  /// </summary>
  /// <remarks>
  /// A channel order is a rearrangement, an opaque alpha is a constant, and grey widened to colour
  /// is three copies of one value: each comes back out exactly. Colour into luminance and
  /// chrominance, alpha dropped, or one subsampling into another would not, and a lossless codec
  /// that quietly did any of them would be lying about what it is.
  /// </remarks>
  private RawImage _TakeLosslessly(RawImage frame) {
    if (frame.Format == this._format)
      return frame;

    var lossless = (this._format, frame.Format) switch {
      (PixelFormat.Rgb24, PixelFormat.Bgr24 or PixelFormat.Gray8) => true,
      (PixelFormat.Rgba32, PixelFormat.Bgra32 or PixelFormat.Argb32 or PixelFormat.Rgb24 or PixelFormat.Bgr24) => true,
      _ => false,
    };

    if (!lossless)
      throw new NotSupportedException(
        $"The stream is coded as {this._format} and this picture is {frame.Format}. Nothing here converts between the two without changing samples, so it is refused rather than coded losslessly as something it is not; convert the picture first, or build the encoder for {frame.Format}.");

    return FastRawImageConverter.Convert(frame, this._format);
  }

  /// <summary>
  /// The planes of a picture as the coded samples: the input's own for luminance and chrominance,
  /// and the JPEG 2000 reversible colour transform (RFC 9043 §3.7.2) for colour.
  /// </summary>
  /// <remarks>
  /// The transform is the exact inverse of what the decoder undoes: the two colour differences are
  /// taken from green, a quarter of their sum — shifted, so it rounds downwards — is added to green,
  /// and the differences are offset by the sample range so they are never negative. That last is what
  /// makes the chrominance planes nine bits wide, and why every plane of a colour stream is coded
  /// modulo nine bits.
  /// </remarks>
  private Ffv1Plane[] _PlanesOf(RawImage source) {
    var parameters = this._parameters;
    var width = source.Width;
    var height = source.Height;
    var data = source.PixelData;
    var count = width * height;

    if (parameters.ColourSpaceType == _COLOUR_SPACE_RGB) {
      var channels = parameters.ExtraPlane ? 4 : 3;
      var planes = new Ffv1Plane[channels];
      for (var plane = 0; plane < channels; ++plane)
        planes[plane] = new(width, height);

      var offset = 1 << _BITS_PER_RAW_SAMPLE;
      for (var i = 0; i < count; ++i) {
        var red = data[i * channels];
        var green = data[i * channels + 1];
        var blue = data[i * channels + 2];

        var blueDifference = blue - green;
        var redDifference = red - green;
        planes[0].Samples[i] = green + ((blueDifference + redDifference) >> 2);
        planes[1].Samples[i] = blueDifference + offset;
        planes[2].Samples[i] = redDifference + offset;
        if (channels == 4)
          planes[3].Samples[i] = data[i * 4 + 3];
      }

      return planes;
    }

    if (!parameters.ChromaPlanes) {
      var luma = new Ffv1Plane(width, height);
      if (!parameters.ExtraPlane) {
        for (var i = 0; i < count; ++i)
          luma.Samples[i] = data[i];

        return [luma];
      }

      var alpha = new Ffv1Plane(width, height);
      for (var i = 0; i < count; ++i) {
        luma.Samples[i] = data[i * 2];
        alpha.Samples[i] = data[i * 2 + 1];
      }

      return [luma, alpha];
    }

    var yuv = new Ffv1Plane[3];
    for (var plane = 0; plane < 3; ++plane) {
      var (planeWidth, planeHeight) = source.GetPlaneDimensions(plane);
      var samples = source.GetPlaneData(plane);
      yuv[plane] = new(planeWidth, planeHeight);
      for (var i = 0; i < samples.Length; ++i)
        yuv[plane].Samples[i] = samples[i];
    }

    return yuv;
  }

  // ============================================================================================
  // The slice grid
  // ============================================================================================

  /// <summary>
  /// The grid ffmpeg picks when nothing is asked for: from two rows upwards, between as many and
  /// twice as many columns, the first whose slices are at most 360 by 288 pixels; one slice where
  /// nothing fits.
  /// </summary>
  private static (int Horizontal, int Vertical) _DefaultGrid(int width, int height, bool subsampled, int horizontalShift, int verticalShift) {
    for (var vertical = 2; vertical <= 32; ++vertical)
      for (var horizontal = vertical; horizontal <= 2 * vertical; ++horizontal) {
        if (horizontal > width || vertical > height)
          continue;

        if (subsampled && (!_CoversEveryChromaSample(width, horizontal, horizontalShift) || !_CoversEveryChromaSample(height, vertical, verticalShift)))
          continue;

        var widest = (width + horizontal - 1) / horizontal;
        var tallest = (height + vertical - 1) / vertical;
        if ((long)widest * tallest > _LARGEST_DEFAULT_SLICE)
          continue;

        return (horizontal, vertical);
      }

    return (1, 1);
  }

  private static void _RefuseGrid(int width, int height, int horizontal, int vertical, bool subsampled, int horizontalShift, int verticalShift) {
    if (horizontal > _MAX_SLICES_PER_AXIS || vertical > _MAX_SLICES_PER_AXIS)
      throw new NotSupportedException($"A slice grid of {horizontal} by {vertical} is asked for, and {_MAX_SLICES_PER_AXIS} is the most in either direction written here.");

    if (horizontal > width || vertical > height)
      throw new NotSupportedException(
        $"A slice grid of {horizontal} by {vertical} is asked for on a {width}x{height} picture, which would leave a slice narrower than a pixel.");

    if (!subsampled)
      return;

    if (!_CoversEveryChromaSample(width, horizontal, horizontalShift))
      throw new NotSupportedException(
        $"A slice grid {horizontal} wide on a picture {width} wide with chrominance subsampled by {1 << horizontalShift} leaves a column of chrominance that no slice codes, which a version 3 reader cannot recover. Choose a grid that divides the picture at even columns.");

    if (!_CoversEveryChromaSample(height, vertical, verticalShift))
      throw new NotSupportedException(
        $"A slice grid {vertical} high on a picture {height} high with chrominance subsampled by {1 << verticalShift} leaves a row of chrominance that no slice codes, which a version 3 reader cannot recover. Choose a grid that divides the picture at even rows.");
  }

  /// <summary>
  /// Whether the chrominance blocks of a row or column of slices, each starting at its slice's
  /// origin shifted down and as wide as its slice rounded up, reach every chrominance sample.
  /// </summary>
  /// <remarks>
  /// They can fail to. A slice that starts at an odd pixel starts its chrominance one sample early,
  /// and the last slice can then end one sample short of the plane. That is the version 3 slice
  /// geometry the specification describes and ffmpeg's decoder implements, and the reason ffmpeg's
  /// encoder refuses those grids too.
  /// </remarks>
  private static bool _CoversEveryChromaSample(int size, int slices, int shift) {
    var block = 1 << shift;
    var chroma = (size + block - 1) >> shift;
    var covered = new bool[chroma];

    for (var slice = 0; slice < slices; ++slice) {
      var start = (int)((long)slice * size / slices);
      var end = (int)((long)(slice + 1) * size / slices);
      if (end <= start)
        return false;

      var first = start >> shift;
      var width = (end - start + block - 1) >> shift;
      for (var i = first; i < first + width && i < chroma; ++i)
        covered[i] = true;
    }

    return Array.TrueForAll(covered, static c => c);
  }
}
