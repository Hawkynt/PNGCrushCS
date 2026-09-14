using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.Codecs.Ffv1;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>Encodes the FFV1 lossless intra-frame codec defined by RFC 9043.</summary>
/// <remarks>
/// Adapted in part from FFmpeg's <c>libavcodec/ffv1enc.c</c> and <c>ffv1enc_template.c</c>, copyright
/// (c) 2003-2016 Michael Niedermayer, LGPL-2.1-or-later; this adaptation is distributed with
/// PNGCrushCS under LGPL-3.0-or-later. RFC 9043 is used as the normative bitstream description and
/// FFmpeg as the interoperability oracle.
/// <para/>
/// Versions 0 and 1 put their parameters in each keyframe and use one whole-picture slice. Version
/// 3 moves those parameters into the container configuration record and permits an independently
/// delimited slice raster with optional CRC protection. Both entropy coders are available: adaptive
/// Golomb-Rice with run mode, and the binary range coder with either the standard state transition
/// table or a stream-provided one. Both standard context models are available as well.
/// <para/>
/// FFV1 has no motion-compensated P/B pictures and no forward or backward image references. A frame
/// whose keyframe flag is clear still codes every sample; it merely continues the adaptive entropy
/// state from the previous decoded frame. <see cref="Ffv1EncoderOptions.KeyFrameInterval"/> controls
/// how often that state is reset.
/// </remarks>
[VerifiedBy(ConformanceOracle.FFmpeg)]
public sealed class Ffv1Encoder : IVideoCodecEncoder<Ffv1Encoder> {

  private static readonly CodecTag _FFV1 = CodecTag.FromCharacters("FFV1");
  private const string _MATROSKA_CODEC_ID = "V_FFV1";

  private const int _MICRO_VERSION = 4;
  private const int _COLOUR_SPACE_YCBCR = 0;
  private const int _COLOUR_SPACE_RGB = 1;
  private const int _PICTURE_STRUCTURE_PROGRESSIVE = 3;
  private const int _MAX_SLICES_PER_AXIS = 256;
  private const int _LARGEST_DEFAULT_SLICE = 360 * 288;

  /// <summary>The first half of the standard eight-bit eleven-level quantiser, as runs of equal entries.</summary>
  private static ReadOnlySpan<int> _ELEVEN_LEVEL_RUNS => [1, 1, 3, 7, 23, 93];

  /// <summary>The first half of the standard eight-bit five-level quantiser.</summary>
  private static ReadOnlySpan<int> _FIVE_LEVEL_RUNS => [1, 3, 124];

  /// <summary>The high-bit-depth nine-level quantiser used by FFmpeg and RFC-compatible implementations.</summary>
  private static ReadOnlySpan<int> _NINE_LEVEL_HIGH_RUNS => [5, 8, 14, 29, 72];

  /// <summary>The high-bit-depth five-level quantiser paired with <see cref="_NINE_LEVEL_HIGH_RUNS"/>.</summary>
  private static ReadOnlySpan<int> _FIVE_LEVEL_HIGH_RUNS => [11, 39, 78];

  /// <summary>A quantiser that switches one context input off.</summary>
  private static ReadOnlySpan<int> _ONE_LEVEL_RUNS => [128];

  private readonly MediaStreamInfo _stream;
  private readonly PixelFormat _format;
  private readonly Ffv1Parameters _parameters;
  private readonly Ffv1EncoderOptions _options;
  private readonly byte[] _defaultZeroState;
  private readonly byte[] _defaultOneState;
  private readonly byte[] _zeroState;
  private readonly byte[] _oneState;

  private byte[][][][]? _rangeStates;
  private Ffv1GolombState[][][]? _golombStates;
  private ulong _frameNumber;

  private Ffv1Encoder(
    MediaStreamInfo stream, PixelFormat format, Ffv1EncoderOptions options, int horizontalSlices, int verticalSlices) {
    var bitsPerRawSample = Ffv1SampleIO.ResolveBitDepth(format, options.BitsPerRawSample);
    this._format = format;
    this._options = options with {
      BitsPerRawSample = bitsPerRawSample,
      HorizontalSlices = horizontalSlices,
      VerticalSlices = verticalSlices,
      StateTransitionDelta = options.StateTransitionDelta is null ? null : (int[])options.StateTransitionDelta.Clone(),
    };

    (this._defaultZeroState, this._defaultOneState) = Ffv1StateTransition.Build([]);

    var (colourSpace, chromaPlanes, horizontalShift, verticalShift, extraPlane) = _Layout(format);
    var coderType = (int)this._options.EntropyCoder;
    var deltas = this._options.StateTransitionDelta ?? [];

    byte[] privateData;
    ReadOnlyMemory<byte> parameterBody;
    bool fromConfigurationRecord;

    if (this._options.Version >= 3) {
      privateData = _WriteConfigurationRecord(
        this._options, colourSpace, chromaPlanes, horizontalShift, verticalShift, extraPlane);
      parameterBody = privateData.AsMemory(..^4);
      fromConfigurationRecord = true;
    } else {
      privateData = [];
      parameterBody = _WriteStandaloneLegacyParameters(
        this._options, colourSpace, chromaPlanes, horizontalShift, verticalShift, extraPlane);
      fromConfigurationRecord = false;
    }

    var states = _FreshStates();
    this._parameters = Ffv1Parameters.Read(
      new Ffv1RangeCoder(parameterBody, this._defaultZeroState, this._defaultOneState),
      states,
      fromConfigurationRecord);

    (this._zeroState, this._oneState) = Ffv1StateTransition.Build(coderType == (int)Ffv1EntropyCoder.RangeCustom ? deltas : []);

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
      CodecPrivateData = privateData,
      Language = stream.Language,
      Name = stream.Name,
    };
  }

  public static string CodecName => "FFV1 (RFC 9043)";

  public static CodecTag Codec => _FFV1;

  /// <summary>Builds the default version 3 range-coder encoder, inferring its sample layout.</summary>
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
        $"Video stream {stream.Index} states {stream.BitsPerPixel} bits per pixel, which is ambiguous or unsupported for FFV1 inference here. Name the raw pixel format outright instead."),
    };

    return Create(stream, format, new Ffv1EncoderOptions());
  }

  /// <summary>
  /// Builds the default version 3 range-coder encoder with a chosen slice grid and keyframe interval.
  /// </summary>
  /// <remarks>This overload is retained as the compact API; use the options overload for coder, version, sample depth and context choices.</remarks>
  public static Ffv1Encoder Create(
    MediaStreamInfo stream, PixelFormat format, int horizontalSlices = 0, int verticalSlices = 0, int keyFrameInterval = 1)
    => Create(stream, format, new() {
      HorizontalSlices = horizontalSlices,
      VerticalSlices = verticalSlices,
      KeyFrameInterval = keyFrameInterval,
    });

  /// <summary>Builds an encoder for one coded sample format and explicit FFV1 bitstream options.</summary>
  public static Ffv1Encoder Create(MediaStreamInfo stream, PixelFormat format, Ffv1EncoderOptions options) {
    ArgumentNullException.ThrowIfNull(stream);
    ArgumentNullException.ThrowIfNull(options);

    if (stream.Kind != MediaStreamKind.Video)
      throw new NotSupportedException($"FFV1 codes video, and stream {stream.Index} is {stream.Kind}.");

    if (stream.Width <= 0 || stream.Height <= 0)
      throw new NotSupportedException(
        $"Video stream {stream.Index} states a picture size of {stream.Width}x{stream.Height}, and FFV1 needs the size before the first picture to describe the stream.");

    if (options.Version is not (0 or 1 or 3))
      throw new ArgumentOutOfRangeException(nameof(options), options.Version, "RFC 9043 defines FFV1 versions 0, 1 and 3; version 2 was never completed.");

    if (!Enum.IsDefined(options.EntropyCoder))
      throw new ArgumentOutOfRangeException(nameof(options), options.EntropyCoder, "The requested FFV1 entropy coder is not defined.");

    if (!Enum.IsDefined(options.ContextModel))
      throw new ArgumentOutOfRangeException(nameof(options), options.ContextModel, "The requested FFV1 context model is not defined.");

    if (options.KeyFrameInterval <= 0)
      throw new ArgumentOutOfRangeException(nameof(options), options.KeyFrameInterval, "An FFV1 keyframe interval must be at least one frame.");

    var coded = _CodedFormat(format);
    var bitsPerRawSample = Ffv1SampleIO.ResolveBitDepth(coded, options.BitsPerRawSample);
    if (options.Version == 0 && bitsPerRawSample != 8)
      throw new NotSupportedException("FFV1 version 0 has no bits-per-raw-sample field and is therefore limited to eight-bit samples.");

    if (bitsPerRawSample > 8 && options.EntropyCoder == Ffv1EntropyCoder.GolombRice)
      throw new NotSupportedException(
        "RFC 9043 says Golomb-Rice should not be used above eight bits and FFmpeg forces such streams to the range coder; deep-sample output is therefore range-coded here for interoperability.");

    if (options.EntropyCoder == Ffv1EntropyCoder.RangeCustom) {
      if (options.StateTransitionDelta is not { Length: 256 })
        throw new ArgumentException("A custom FFV1 range coder needs exactly 256 state-transition deltas.", nameof(options));

      for (var i = 1; i < options.StateTransitionDelta.Length; ++i)
        if (options.StateTransitionDelta[i] is < -255 or > 255)
          throw new ArgumentOutOfRangeException(nameof(options), options.StateTransitionDelta[i], $"State-transition delta {i} is outside the canonical -255..255 range.");
    } else if (options.StateTransitionDelta is { Length: > 0 })
      throw new ArgumentException("State-transition deltas are meaningful only with the custom range coder.", nameof(options));

    var (colourSpace, chromaPlanes, horizontalShift, verticalShift, _) = _Layout(coded);
    var subsampled = chromaPlanes && colourSpace == _COLOUR_SPACE_YCBCR;

    int horizontalSlices;
    int verticalSlices;
    if (options.Version >= 3) {
      if ((options.HorizontalSlices <= 0) != (options.VerticalSlices <= 0))
        throw new ArgumentException("A slice grid is stated as both its width and its height, or as neither.", nameof(options));

      (horizontalSlices, verticalSlices) = options.HorizontalSlices <= 0
        ? _DefaultGrid(stream.Width, stream.Height, subsampled, horizontalShift, verticalShift)
        : (options.HorizontalSlices, options.VerticalSlices);

      _RefuseGrid(
        stream.Width, stream.Height, horizontalSlices, verticalSlices,
        subsampled, horizontalShift, verticalShift);
    } else {
      if ((options.HorizontalSlices, options.VerticalSlices) is not ((0, 0) or (1, 1)))
        throw new NotSupportedException($"FFV1 version {options.Version} has one whole-picture slice and cannot write a {options.HorizontalSlices}x{options.VerticalSlices} slice grid.");

      horizontalSlices = verticalSlices = 1;
    }

    return new(stream, coded, options with { BitsPerRawSample = bitsPerRawSample }, horizontalSlices, verticalSlices);
  }

  /// <summary>Turns one picture into one complete FFV1 frame.</summary>
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
    var keyframe = this._frameNumber % (ulong)this._options.KeyFrameInterval == 0;
    var data = this._parameters.Version >= 3
      ? this._EncodeVersion3Frame(planes, keyframe)
      : this._EncodeLegacyFrame(planes, keyframe);
    ++this._frameNumber;

    packet = new(
      StreamIndex: this._stream.Index,
      Data: data,
      PresentationTimestamp: presentationTimestamp,
      DecodeTimestamp: presentationTimestamp,
      IsKeyFrame: keyframe);
    return true;
  }

  public MediaStreamInfo DescribeStream() => this._stream;

  // ============================================================================================
  // Parameters
  // ============================================================================================

  private static byte[] _WriteConfigurationRecord(
    Ffv1EncoderOptions options, int colourSpace, bool chromaPlanes, int horizontalShift, int verticalShift, bool extraPlane) {
    var (zero, one) = Ffv1StateTransition.Build([]);
    var coder = new Ffv1RangeEncoder(zero, one);
    _WriteParameters(coder, options, colourSpace, chromaPlanes, horizontalShift, verticalShift, extraPlane);

    var body = coder.Terminate(false);
    var record = new byte[body.Length + 4];
    body.CopyTo(record, 0);
    BinaryPrimitives.WriteUInt32BigEndian(record.AsSpan(body.Length), Ffv1Crc.Of(body));
    return record;
  }

  private static byte[] _WriteStandaloneLegacyParameters(
    Ffv1EncoderOptions options, int colourSpace, bool chromaPlanes, int horizontalShift, int verticalShift, bool extraPlane) {
    var (zero, one) = Ffv1StateTransition.Build([]);
    var coder = new Ffv1RangeEncoder(zero, one);
    _WriteParameters(coder, options, colourSpace, chromaPlanes, horizontalShift, verticalShift, extraPlane);
    return coder.Terminate(false);
  }

  /// <summary>Writes the common FFV1 parameter sequence in its version-specific form.</summary>
  private static void _WriteParameters(
    Ffv1RangeEncoder coder, Ffv1EncoderOptions options,
    int colourSpace, bool chromaPlanes, int horizontalShift, int verticalShift, bool extraPlane) {
    var states = _FreshStates();
    coder.Symbol(states, options.Version, false);
    if (options.Version >= 3)
      coder.Symbol(states, _MICRO_VERSION, false);

    coder.Symbol(states, (int)options.EntropyCoder, false);
    if (options.EntropyCoder == Ffv1EntropyCoder.RangeCustom)
      for (var i = 1; i < 256; ++i)
        coder.Symbol(states, options.StateTransitionDelta![i], true);

    coder.Symbol(states, colourSpace, false);
    if (options.Version >= 1)
      coder.Symbol(states, options.BitsPerRawSample, false);

    coder.Put(states, 0, chromaPlanes ? 1 : 0);
    coder.Symbol(states, horizontalShift, false);
    coder.Symbol(states, verticalShift, false);
    coder.Put(states, 0, extraPlane ? 1 : 0);

    if (options.Version >= 3) {
      coder.Symbol(states, options.HorizontalSlices - 1, false);
      coder.Symbol(states, options.VerticalSlices - 1, false);
      coder.Symbol(states, 1, false); // one table set
    }

    _WriteQuantTableSet(coder, options.ContextModel, options.BitsPerRawSample > 8);

    if (options.Version < 3)
      return;

    coder.Put(states, 0, 0); // no explicitly trained initial range states
    coder.Symbol(states, options.SliceCrc ? 1 : 0, false);
    coder.Symbol(states, options.KeyFrameInterval == 1 ? 1 : 0, false);
  }

  private static void _WriteQuantTableSet(Ffv1RangeEncoder coder, Ffv1ContextModel model, bool deepSamples) {
    var primary = deepSamples ? _NINE_LEVEL_HIGH_RUNS : _ELEVEN_LEVEL_RUNS;
    var secondary = deepSamples ? _FIVE_LEVEL_HIGH_RUNS : _FIVE_LEVEL_RUNS;

    _WriteQuantTable(coder, primary);
    _WriteQuantTable(coder, primary);
    _WriteQuantTable(coder, model == Ffv1ContextModel.Small ? primary : secondary);
    _WriteQuantTable(coder, model == Ffv1ContextModel.Small ? _ONE_LEVEL_RUNS : secondary);
    _WriteQuantTable(coder, model == Ffv1ContextModel.Small ? _ONE_LEVEL_RUNS : secondary);
  }

  private static void _WriteQuantTable(Ffv1RangeEncoder coder, ReadOnlySpan<int> runs) {
    var states = _FreshStates();
    foreach (var run in runs)
      coder.Symbol(states, run - 1, false);
  }

  // ============================================================================================
  // Frames and slices
  // ============================================================================================

  private byte[] _EncodeVersion3Frame(Ffv1Plane[] planes, bool keyframe) {
    var output = new MemoryStream();
    var sliceEncoder = new Ffv1SliceEncoder(this._parameters);
    var frameCoder = new Ffv1RangeEncoder(this._defaultZeroState, this._defaultOneState);
    var keyframeState = _FreshStates();
    frameCoder.Put(keyframeState, 0, keyframe ? 1 : 0);

    if (this._parameters.CoderType == (int)Ffv1EntropyCoder.RangeCustom)
      frameCoder.UseStateTransitions(this._zeroState, this._oneState);

    var sliceIndex = 0;
    for (var sliceY = 0; sliceY < this._parameters.VerticalSlices; ++sliceY)
      for (var sliceX = 0; sliceX < this._parameters.HorizontalSlices; ++sliceX, ++sliceIndex) {
        var coder = sliceIndex == 0
          ? frameCoder
          : new Ffv1RangeEncoder(this._zeroState, this._oneState);

        var slicePlanes = this._WriteSliceHeaderAndCutOut(coder, planes, sliceX, sliceY);
        byte[] body;

        if (this._parameters.CoderType == (int)Ffv1EntropyCoder.GolombRice) {
          var header = coder.Terminate(true);
          var golomb = new Ffv1GolombEncoder();
          this._EncodeGolombSamples(sliceEncoder, golomb, slicePlanes, sliceIndex, keyframe);
          body = _Concat(header, golomb.ToArray());
        } else {
          this._EncodeRangeSamples(sliceEncoder, coder, slicePlanes, sliceIndex, keyframe);
          body = coder.Terminate(true);
        }

        _AppendSlice(output, body, this._parameters.ErrorCorrection != 0);
      }

    return output.ToArray();
  }

  private byte[] _EncodeLegacyFrame(Ffv1Plane[] planes, bool keyframe) {
    var coder = new Ffv1RangeEncoder(this._defaultZeroState, this._defaultOneState);
    var keyframeState = _FreshStates();
    coder.Put(keyframeState, 0, keyframe ? 1 : 0);

    if (keyframe) {
      var (colourSpace, chromaPlanes, horizontalShift, verticalShift, extraPlane) = _Layout(this._format);
      _WriteParameters(coder, this._options, colourSpace, chromaPlanes, horizontalShift, verticalShift, extraPlane);
    }

    if (this._parameters.CoderType == (int)Ffv1EntropyCoder.RangeCustom)
      coder.UseStateTransitions(this._zeroState, this._oneState);

    var sliceEncoder = new Ffv1SliceEncoder(this._parameters);
    if (this._parameters.CoderType != (int)Ffv1EntropyCoder.GolombRice) {
      this._EncodeRangeSamples(sliceEncoder, coder, planes, 0, keyframe);
      return coder.Terminate(true);
    }

    var header = coder.Terminate(false);
    var golomb = new Ffv1GolombEncoder();
    this._EncodeGolombSamples(sliceEncoder, golomb, planes, 0, keyframe);
    return _Concat(header, golomb.ToArray());
  }

  /// <summary>Writes a version 3 slice header and returns the samples covered by that slice.</summary>
  private Ffv1Plane[] _WriteSliceHeaderAndCutOut(Ffv1RangeEncoder coder, Ffv1Plane[] planes, int sliceX, int sliceY) {
    var headerStates = _FreshStates();
    coder.Symbol(headerStates, sliceX, false);
    coder.Symbol(headerStates, sliceY, false);
    coder.Symbol(headerStates, 0, false); // one grid cell wide
    coder.Symbol(headerStates, 0, false); // one grid cell high
    for (var i = 0; i < this._parameters.QuantTableSetIndexCount; ++i)
      coder.Symbol(headerStates, 0, false);

    coder.Symbol(headerStates, _PICTURE_STRUCTURE_PROGRESSIVE, false);
    coder.Symbol(headerStates, 0, false); // sample aspect ratio unknown
    coder.Symbol(headerStates, 0, false);

    var x = (int)((long)sliceX * this._stream.Width / this._parameters.HorizontalSlices);
    var y = (int)((long)sliceY * this._stream.Height / this._parameters.VerticalSlices);
    var width = (int)((long)(sliceX + 1) * this._stream.Width / this._parameters.HorizontalSlices) - x;
    var height = (int)((long)(sliceY + 1) * this._stream.Height / this._parameters.VerticalSlices) - y;

    var result = new Ffv1Plane[planes.Length];
    for (var plane = 0; plane < planes.Length; ++plane)
      result[plane] = this._CutOut(planes[plane], plane, x, y, width, height);

    return result;
  }

  private void _EncodeRangeSamples(
    Ffv1SliceEncoder encoder, Ffv1RangeEncoder coder, Ffv1Plane[] planes, int slice, bool keyframe) {
    var states = this._RangeContextsForSlice(slice, keyframe);

    if (this._parameters.ColourSpaceType == _COLOUR_SPACE_YCBCR) {
      for (var plane = 0; plane < planes.Length; ++plane)
        encoder.EncodePlane(coder, planes[plane], states[this._parameters.PlaneKindOf(plane)], 0);
      return;
    }

    for (var line = 0; line < planes[0].Height; ++line)
      for (var plane = 0; plane < planes.Length; ++plane)
        encoder.EncodeLine(coder, planes[plane], line, states[this._parameters.PlaneKindOf(plane)], 0);
  }

  private void _EncodeGolombSamples(
    Ffv1SliceEncoder encoder, Ffv1GolombEncoder coder, Ffv1Plane[] planes, int slice, bool keyframe) {
    var states = this._GolombContextsForSlice(slice, keyframe);

    if (this._parameters.ColourSpaceType == _COLOUR_SPACE_YCBCR) {
      for (var plane = 0; plane < planes.Length; ++plane)
        encoder.EncodePlane(coder, planes[plane], states[this._parameters.PlaneKindOf(plane)], 0);
      return;
    }

    var runIndex = 0;
    for (var line = 0; line < planes[0].Height; ++line)
      for (var plane = 0; plane < planes.Length; ++plane)
        encoder.EncodeLine(coder, planes[plane], line, states[this._parameters.PlaneKindOf(plane)], 0, ref runIndex);
  }

  private static void _AppendSlice(MemoryStream output, byte[] slice, bool checksum) {
    if (slice.Length >= 1 << 24)
      throw new InvalidDataException($"An FFV1 slice is {slice.Length} bytes; its 24-bit length field can describe at most {(1 << 24) - 1}.");

    if (!checksum) {
      output.Write(slice);
      output.WriteByte((byte)(slice.Length >> 16));
      output.WriteByte((byte)(slice.Length >> 8));
      output.WriteByte((byte)slice.Length);
      return;
    }

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
    var subsampled = this._parameters.ColourSpaceType == _COLOUR_SPACE_YCBCR && this._parameters.ChromaPlanes && plane is 1 or 2;
    var horizontal = subsampled ? this._parameters.ChromaHorizontalShift : 0;
    var vertical = subsampled ? this._parameters.ChromaVerticalShift : 0;

    var x0 = x >> horizontal;
    var y0 = y >> vertical;
    var cut = new Ffv1Plane((width + (1 << horizontal) - 1) >> horizontal, (height + (1 << vertical) - 1) >> vertical);
    for (var row = 0; row < cut.Height; ++row)
      for (var column = 0; column < cut.Width; ++column)
        cut[column, row] = source[x0 + column, y0 + row];

    return cut;
  }

  private static byte[] _Concat(byte[] first, byte[] second) {
    var result = new byte[checked(first.Length + second.Length)];
    first.CopyTo(result, 0);
    second.CopyTo(result, first.Length);
    return result;
  }

  // ============================================================================================
  // Adaptive states
  // ============================================================================================

  private byte[][][] _RangeContextsForSlice(int slice, bool keyframe) {
    var sliceCount = checked(this._parameters.HorizontalSlices * this._parameters.VerticalSlices);
    this._rangeStates ??= new byte[sliceCount][][][];
    if (keyframe || this._rangeStates[slice] == null)
      this._rangeStates[slice] = this._FreshRangeContexts();

    return this._rangeStates[slice];
  }

  private Ffv1GolombState[][] _GolombContextsForSlice(int slice, bool keyframe) {
    var sliceCount = checked(this._parameters.HorizontalSlices * this._parameters.VerticalSlices);
    this._golombStates ??= new Ffv1GolombState[sliceCount][][];
    if (keyframe || this._golombStates[slice] == null)
      this._golombStates[slice] = this._FreshGolombContexts();

    return this._golombStates[slice];
  }

  private byte[][][] _FreshRangeContexts() {
    var kinds = new byte[3][][];
    for (var plane = 0; plane < this._parameters.PlaneCount; ++plane) {
      var kind = this._parameters.PlaneKindOf(plane);
      if (kinds[kind] != null)
        continue;

      var contexts = new byte[this._parameters.ContextCount[0]][];
      for (var context = 0; context < contexts.Length; ++context)
        contexts[context] = _FreshStates();

      kinds[kind] = contexts;
    }

    return kinds;
  }

  private Ffv1GolombState[][] _FreshGolombContexts() {
    var kinds = new Ffv1GolombState[3][];
    for (var plane = 0; plane < this._parameters.PlaneCount; ++plane) {
      var kind = this._parameters.PlaneKindOf(plane);
      if (kinds[kind] != null)
        continue;

      var contexts = new Ffv1GolombState[this._parameters.ContextCount[0]];
      for (var context = 0; context < contexts.Length; ++context)
        contexts[context] = new();

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
  // Input samples
  // ============================================================================================

  private static (int ColourSpace, bool ChromaPlanes, int HorizontalShift, int VerticalShift, bool ExtraPlane) _Layout(PixelFormat format) => format switch {
    PixelFormat.Gray8 or PixelFormat.Gray10 or PixelFormat.Gray16 => (_COLOUR_SPACE_YCBCR, false, 0, 0, false),
    PixelFormat.GrayAlpha16 or PixelFormat.GrayAlpha32 => (_COLOUR_SPACE_YCBCR, false, 0, 0, true),
    PixelFormat.Yuv420P8 or PixelFormat.Yuv420P10 or PixelFormat.Yuv420P12 or PixelFormat.Yuv420P16 => (_COLOUR_SPACE_YCBCR, true, 1, 1, false),
    PixelFormat.Yuv422P8 or PixelFormat.Yuv422P10 or PixelFormat.Yuv422P12 or PixelFormat.Yuv422P16 => (_COLOUR_SPACE_YCBCR, true, 1, 0, false),
    PixelFormat.Yuv440P8 or PixelFormat.Yuv440P10 or PixelFormat.Yuv440P12 or PixelFormat.Yuv440P16 => (_COLOUR_SPACE_YCBCR, true, 0, 1, false),
    PixelFormat.Yuv444P8 or PixelFormat.Yuv444P10 or PixelFormat.Yuv444P12 or PixelFormat.Yuv444P16 => (_COLOUR_SPACE_YCBCR, true, 0, 0, false),
    PixelFormat.Rgb24 or PixelFormat.Rgb48 => (_COLOUR_SPACE_RGB, true, 0, 0, false),
    PixelFormat.Rgba32 or PixelFormat.Rgba64 => (_COLOUR_SPACE_RGB, true, 0, 0, true),
    _ => throw new NotSupportedException(
      $"{format} is not a raw integer format FFV1 is written in here. Supported layouts are grey, grey-alpha, planar 4:2:0/4:2:2/4:4:0/4:4:4, and RGB/RGBA at eight through sixteen significant bits."),
  };

  private static PixelFormat _CodedFormat(PixelFormat format) {
    var coded = format switch {
      PixelFormat.Bgr24 => PixelFormat.Rgb24,
      PixelFormat.Bgra32 or PixelFormat.Argb32 => PixelFormat.Rgba32,
      _ => format,
    };

    _ = _Layout(coded);
    return coded;
  }

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
        $"The stream is coded as {this._format} and this picture is {frame.Format}. Nothing here converts between the two without changing sample values, so it is refused rather than coded losslessly as something it is not; convert the picture first, or build the encoder for {frame.Format}.");

    return FastRawImageConverter.Convert(frame, this._format);
  }

  private Ffv1Plane[] _PlanesOf(RawImage source) {
    var width = source.Width;
    var height = source.Height;
    var data = source.PixelData.AsSpan();
    var count = checked(width * height);
    var bits = this._parameters.BitsPerRawSample;
    var sampleMask = (1 << bits) - 1;

    if (this._parameters.ColourSpaceType == _COLOUR_SPACE_RGB) {
      var channels = this._parameters.ExtraPlane ? 4 : 3;
      var planes = new Ffv1Plane[channels];
      for (var plane = 0; plane < channels; ++plane)
        planes[plane] = new(width, height);

      var offset = 1 << bits;
      var oldRgbTransform = bits is >= 9 and <= 15 && !this._parameters.ExtraPlane;
      for (var i = 0; i < count; ++i) {
        var red = _ReadCheckedSample(data, i * channels, source.Format, sampleMask);
        var green = _ReadCheckedSample(data, i * channels + 1, source.Format, sampleMask);
        var blue = _ReadCheckedSample(data, i * channels + 2, source.Format, sampleMask);

        if (oldRgbTransform) {
          var greenDifference = green - blue;
          var redDifference = red - blue;
          planes[0].Samples[i] = blue + ((greenDifference + redDifference) >> 2);
          planes[1].Samples[i] = greenDifference + offset;
          planes[2].Samples[i] = redDifference + offset;
        } else {
          var blueDifference = blue - green;
          var redDifference = red - green;
          planes[0].Samples[i] = green + ((blueDifference + redDifference) >> 2);
          planes[1].Samples[i] = blueDifference + offset;
          planes[2].Samples[i] = redDifference + offset;
        }

        if (channels == 4)
          planes[3].Samples[i] = _ReadCheckedSample(data, i * 4 + 3, source.Format, sampleMask);
      }

      return planes;
    }

    if (!this._parameters.ChromaPlanes) {
      var luma = new Ffv1Plane(width, height);
      if (!this._parameters.ExtraPlane) {
        for (var i = 0; i < count; ++i)
          luma.Samples[i] = _ReadCheckedSample(data, i, source.Format, sampleMask);

        return [luma];
      }

      var alpha = new Ffv1Plane(width, height);
      for (var i = 0; i < count; ++i) {
        luma.Samples[i] = _ReadCheckedSample(data, i * 2, source.Format, sampleMask);
        alpha.Samples[i] = _ReadCheckedSample(data, i * 2 + 1, source.Format, sampleMask);
      }

      return [luma, alpha];
    }

    var yuv = new Ffv1Plane[3];
    for (var plane = 0; plane < 3; ++plane) {
      var (planeWidth, planeHeight) = source.GetPlaneDimensions(plane);
      var samples = source.GetPlaneData(plane);
      var sampleCount = checked(planeWidth * planeHeight);
      yuv[plane] = new(planeWidth, planeHeight);
      for (var i = 0; i < sampleCount; ++i)
        yuv[plane].Samples[i] = _ReadCheckedSample(samples, i, source.Format, sampleMask);
    }

    return yuv;
  }

  private static int _ReadCheckedSample(ReadOnlySpan<byte> data, int sampleIndex, PixelFormat format, int mask) {
    var value = Ffv1SampleIO.ReadSample(data, sampleIndex, format);
    if ((value & ~mask) != 0)
      throw new InvalidDataException($"{format} sample {sampleIndex} has value {value}, outside the 0..{mask} range stated for this FFV1 stream.");

    return value;
  }

  // ============================================================================================
  // Slice grid
  // ============================================================================================

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
    if (horizontal <= 0 || vertical <= 0)
      throw new NotSupportedException($"A slice grid must have at least one row and column, not {horizontal} by {vertical}.");

    if (horizontal > _MAX_SLICES_PER_AXIS || vertical > _MAX_SLICES_PER_AXIS)
      throw new NotSupportedException($"A slice grid of {horizontal} by {vertical} is asked for, and {_MAX_SLICES_PER_AXIS} is the most in either direction written here.");

    if (horizontal > width || vertical > height)
      throw new NotSupportedException(
        $"A slice grid of {horizontal} by {vertical} is asked for on a {width}x{height} picture, which would leave a slice narrower than a pixel.");

    if (!subsampled)
      return;

    if (!_CoversEveryChromaSample(width, horizontal, horizontalShift))
      throw new NotSupportedException(
        $"A slice grid {horizontal} wide on a picture {width} wide with chrominance subsampled by {1 << horizontalShift} leaves a column of chrominance that no slice codes. Choose a grid that divides the picture at compatible columns.");

    if (!_CoversEveryChromaSample(height, vertical, verticalShift))
      throw new NotSupportedException(
        $"A slice grid {vertical} high on a picture {height} high with chrominance subsampled by {1 << verticalShift} leaves a row of chrominance that no slice codes. Choose a grid that divides the picture at compatible rows.");
  }

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