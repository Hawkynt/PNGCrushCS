using System;
using System.Collections.Generic;
using System.IO;
using FileFormat.Bmp;
using FileFormat.Codecs.Ffv1;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>
/// Decodes FFV1, the lossless intra-frame codec of RFC 9043, versions 0, 1 and 3.
/// </summary>
/// <remarks>
/// No transform and no quantiser: every sample is predicted from the median of three neighbours and
/// the difference is entropy coded in a context chosen by five more. What makes it interesting is
/// that everything about the coding is in the stream — the context quantisers, the states the
/// contexts start at, and even the range coder's state transition table can all be replaced by a
/// file that says so.
/// <para/>
/// Versions 0 and 1 put their parameters inside keyframes. Version 3 moves them into a checked
/// configuration record and frames become a sequence of independently delimited slices. A frame
/// with its keyframe flag clear still contains every image sample; only adaptive entropy state is
/// inherited from the preceding decoded frame.
/// </remarks>
public sealed class Ffv1Decoder : IVideoCodecDecoder<Ffv1Decoder> {

  private static readonly CodecTag _FFV1 = CodecTag.FromCharacters("FFV1");
  private const string _MATROSKA_CODEC_ID = "V_FFV1";
  private const int _FOOTER_LENGTH = 3;
  private const int _FOOTER_LENGTH_WITH_CHECKSUM = 8;
  private const int _MAX_SLICES = 1024;

  private readonly int _width;
  private readonly int _height;
  private readonly Ffv1Parameters? _configured;

  private Ffv1Parameters? _stated;
  private byte[][][][]? _rangeStates;
  private Ffv1GolombState[][][]? _golombStates;

  private Ffv1Decoder(int width, int height, Ffv1Parameters? configured) {
    this._width = width;
    this._height = height;
    this._configured = configured;
  }

  public static string CodecName => "FFV1 (RFC 9043)";

  public static bool Accepts(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);

    return stream.Kind == MediaStreamKind.Video
           && (stream.Codec.EqualsIgnoringCase(_FFV1)
               || string.Equals(stream.CodecId, _MATROSKA_CODEC_ID, StringComparison.OrdinalIgnoreCase));
  }

  public static Ffv1Decoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);

    if (stream.Width <= 0 || stream.Height <= 0)
      throw new InvalidDataException(
        $"Video stream {stream.Index} states a picture size of {stream.Width}x{stream.Height}, which no frame can be decoded into.");

    var record = _ConfigurationRecord(stream);
    return new(stream.Width, stream.Height, record.IsEmpty ? null : _ReadConfigurationRecord(record, stream.Index));
  }

  private static ReadOnlyMemory<byte> _ConfigurationRecord(MediaStreamInfo stream) {
    var description = stream.CodecPrivateData;
    if (description.IsEmpty)
      return ReadOnlyMemory<byte>.Empty;

    if (string.Equals(stream.CodecId, _MATROSKA_CODEC_ID, StringComparison.OrdinalIgnoreCase))
      return description;

    return description.Length > BitmapInfoHeader.StructSize ? description[BitmapInfoHeader.StructSize..] : ReadOnlyMemory<byte>.Empty;
  }

  private static Ffv1Parameters _ReadConfigurationRecord(ReadOnlyMemory<byte> record, int streamIndex) {
    if (record.Length < 5)
      throw new InvalidDataException(
        $"Video stream {streamIndex} carries {record.Length} bytes of configuration record, which is shorter than the four bytes of checksum at the end of one.");

    if (Ffv1Crc.Of(record.Span) != 0)
      throw new InvalidDataException(
        $"Video stream {streamIndex} carries a configuration record whose checksum does not come out, so what it says about the stream cannot be trusted.");

    var states = _FreshStates();
    var (zero, one) = Ffv1StateTransition.Build([]);
    var coder = new Ffv1RangeCoder(record[..^4], zero, one);
    var parameters = Ffv1Parameters.Read(coder, states, true);

    _RefuseUnread(parameters, streamIndex);
    return parameters;
  }

  /// <summary>Turns one packet into the picture it codes.</summary>
  public bool TryDecode(CodedPacket packet, out RawImage frame) {
    var data = packet.Data;
    if (data.IsEmpty)
      throw new InvalidDataException("A frame of no bytes cannot be decoded, and a repeat of the frame before it is not what a frame of no bytes means.");

    var parameters = this._configured;
    List<Ffv1Slice>? slices = null;
    ReadOnlyMemory<byte> firstCoderData = data;

    if (parameters != null) {
      slices = _SlicePositions(parameters, this._width, this._height, data);
      var first = slices[0];
      firstCoderData = data.Slice(first.Offset, first.PayloadLength);
    }

    // The frame's keyframe bit is always read with the default state transition table. A custom
    // table, when configured, takes effect only after this bit (and after legacy parameters state it).
    var (zero, one) = Ffv1StateTransition.Build([]);
    var coder = new Ffv1RangeCoder(firstCoderData, zero, one);
    var keyframeState = _FreshStates();
    var keyframe = coder.Get(keyframeState, 0) != 0;

    if (parameters == null) {
      if (keyframe) {
        var headerStates = _FreshStates();
        this._stated = Ffv1Parameters.Read(coder, headerStates, false);
        _RefuseUnread(this._stated, 0);
      }

      parameters = this._stated
                   ?? throw new InvalidDataException(
                     "The stream opens with a frame that is not a keyframe, and a version 0 or 1 stream states how it is coded only in its keyframes.");
    }

    if (parameters.HasStateTransitionDelta) {
      (zero, one) = Ffv1StateTransition.Build(parameters.StateTransitionDelta);
      coder.UseStateTransitions(zero, one);
    }

    if (!keyframe && parameters.IntraOnly)
      throw new InvalidDataException("The FFV1 configuration record says every frame is a keyframe, but this frame says it is not one.");

    if (!keyframe && !this._HasCodingState(parameters))
      throw new InvalidDataException(
        "This FFV1 frame is not a keyframe and depends on entropy-coder state from an earlier frame. Start decoding at a keyframe instead.");

    frame = this._DecodeFrame(parameters, data, coder, keyframe, zero, one, slices);
    return true;
  }

  private bool _HasCodingState(Ffv1Parameters parameters)
    => parameters.CoderType == 0 ? this._golombStates != null : this._rangeStates != null;

  // ============================================================================================
  // The frame
  // ============================================================================================

  private RawImage _DecodeFrame(
    Ffv1Parameters parameters, ReadOnlyMemory<byte> data, Ffv1RangeCoder frameCoder, bool keyframe,
    byte[] zero, byte[] one, List<Ffv1Slice>? knownSlices) {
    var planes = this._AllocatePlanes(parameters);
    var slices = knownSlices ?? _SlicePositions(parameters, this._width, this._height, data);
    var coverage = parameters.Version >= 3
      ? new bool[checked(parameters.HorizontalSlices * parameters.VerticalSlices)]
      : null;

    for (var index = 0; index < slices.Count; ++index) {
      var slice = slices[index];
      var body = data.Slice(slice.Offset, slice.PayloadLength);
      var coder = index == 0 ? frameCoder : new Ffv1RangeCoder(body, zero, one);

      this._DecodeSlice(parameters, coder, body, slice, planes, keyframe, index, slices.Count, coverage);
    }

    if (coverage != null && Array.Exists(coverage, static covered => !covered))
      throw new InvalidDataException("The FFV1 slices do not cover the complete configured slice raster.");

    return this._Compose(parameters, planes);
  }

  private void _DecodeSlice(
    Ffv1Parameters parameters, Ffv1RangeCoder coder, ReadOnlyMemory<byte> body, Ffv1Slice slice, Ffv1Plane[] planes,
    bool keyframe, int index, int sliceCount, bool[]? coverage) {
    var tableSetIndices = new int[3];
    var geometry = slice;

    if (parameters.Version >= 3) {
      var headerStates = _FreshStates();

      var sliceX = coder.Symbol(headerStates, false);
      var sliceY = coder.Symbol(headerStates, false);
      var sliceWidth = coder.Symbol(headerStates, false) + 1;
      var sliceHeight = coder.Symbol(headerStates, false) + 1;

      for (var i = 0; i < parameters.QuantTableSetIndexCount; ++i) {
        var stated = coder.Symbol(headerStates, false);
        if ((uint)stated >= (uint)parameters.QuantTableSetCount)
          throw new InvalidDataException($"A slice names quantisation table set {stated}, where the stream states {parameters.QuantTableSetCount}.");

        if (i < tableSetIndices.Length)
          tableSetIndices[i] = stated;
      }

      coder.Symbol(headerStates, false); // picture structure
      coder.Symbol(headerStates, false); // sample aspect ratio numerator
      coder.Symbol(headerStates, false); // sample aspect ratio denominator

      geometry = _GeometryOf(parameters, this._width, this._height, sliceX, sliceY, sliceWidth, sliceHeight, slice);
      _MarkCoverage(parameters, coverage!, sliceX, sliceY, sliceWidth, sliceHeight);
    }

    var decoder = new Ffv1SliceDecoder(parameters);
    var golomb = parameters.CoderType == 0 ? this._StartGolomb(parameters, coder, body) : null;
    this._PrepareStates(parameters, tableSetIndices, keyframe, index, sliceCount);

    var slicePlanes = new Ffv1Plane[parameters.PlaneCount];
    for (var plane = 0; plane < slicePlanes.Length; ++plane) {
      var (width, height) = _PlaneSize(parameters, plane, geometry.PixelWidth, geometry.PixelHeight);
      slicePlanes[plane] = new(width, height);
    }

    var runIndex = 0;

    if (parameters.ColourSpaceType == 0) {
      for (var plane = 0; plane < slicePlanes.Length; ++plane) {
        var tableSet = parameters.TableSetIndexOf(plane, tableSetIndices);
        runIndex = 0;
        if (golomb == null)
          decoder.DecodePlane(coder, slicePlanes[plane], this._rangeStates![index][parameters.PlaneKindOf(plane)], tableSet);
        else
          decoder.DecodePlane(golomb, slicePlanes[plane], this._golombStates![index][parameters.PlaneKindOf(plane)], tableSet, ref runIndex);
      }
    } else {
      for (var y = 0; y < geometry.PixelHeight; ++y)
        for (var plane = 0; plane < slicePlanes.Length; ++plane) {
          var tableSet = parameters.TableSetIndexOf(plane, tableSetIndices);
          if (golomb == null)
            decoder.DecodeLine(coder, slicePlanes[plane], y, this._rangeStates![index][parameters.PlaneKindOf(plane)], tableSet);
          else
            decoder.DecodeLine(golomb, slicePlanes[plane], y, this._golombStates![index][parameters.PlaneKindOf(plane)], tableSet, ref runIndex);
        }
    }

    if (parameters.ColourSpaceType == 1)
      _UndoColourTransform(parameters, slicePlanes);

    _Blit(parameters, slicePlanes, planes, geometry);
  }

  private Ffv1GolombDecoder _StartGolomb(Ffv1Parameters parameters, Ffv1RangeCoder coder, ReadOnlyMemory<byte> body) {
    if (parameters.Version >= 3 && parameters.MicroVersion > 1)
      coder.ReadTerminator();

    var startByte = coder.BytesRead - 1;
    if ((uint)startByte >= (uint)body.Length)
      throw new InvalidDataException(
        $"The range-coded FFV1 header consumed {coder.BytesRead} byte(s), leaving no physical byte at which Golomb-Rice sample data can begin.");

    return new(body, startByte);
  }

  // ============================================================================================
  // Slices
  // ============================================================================================

  private readonly record struct Ffv1Slice(
    int Offset, int PayloadLength, int TotalLength, int PixelX, int PixelY, int PixelWidth, int PixelHeight);

  private static List<Ffv1Slice> _SlicePositions(Ffv1Parameters parameters, int width, int height, ReadOnlyMemory<byte> data) {
    if (parameters.Version <= 1)
      return [new(0, data.Length, data.Length, 0, 0, width, height)];

    var footer = parameters.ErrorCorrection != 0 ? _FOOTER_LENGTH_WITH_CHECKSUM : _FOOTER_LENGTH;
    var maxCount = checked(parameters.HorizontalSlices * parameters.VerticalSlices);
    var slices = new List<Ffv1Slice>(Math.Min(maxCount, 16));
    var end = data.Length;

    while (end > 0 && slices.Count < maxCount) {
      if (end < footer)
        throw new InvalidDataException(
          $"An FFV1 frame ends with only {end} byte(s), shorter than its {footer}-byte slice footer.");

      var span = data.Span;
      var stated = (span[end - footer] << 16) | (span[end - footer + 1] << 8) | span[end - footer + 2];
      if (stated <= 0)
        throw new InvalidDataException("An FFV1 slice states a zero-byte payload, which cannot contain its mandatory slice header.");

      var total = checked(stated + footer);
      if (total > end)
        throw new InvalidDataException(
          $"An FFV1 slice states {stated} payload byte(s) plus a {footer}-byte footer where only {end} byte(s) remain in the frame.");

      var offset = end - total;
      if (parameters.ErrorCorrection != 0 && Ffv1Crc.Of(data.Span.Slice(offset, total)) != 0)
        throw new InvalidDataException($"An FFV1 slice of {total} bytes has a checksum that does not come out, so the picture it holds is damaged.");

      slices.Add(new(offset, stated, total, 0, 0, 0, 0));
      end = offset;
    }

    if (end != 0)
      throw new InvalidDataException(
        $"The FFV1 slice footer chain leaves {end} leading byte(s) unaccounted for or contains more than the configured maximum of {maxCount} slices.");

    if (slices.Count == 0)
      throw new InvalidDataException("The FFV1 frame contains no complete slice.");

    slices.Reverse();
    return slices;
  }

  private static Ffv1Slice _GeometryOf(
    Ffv1Parameters parameters, int frameWidth, int frameHeight,
    int sliceX, int sliceY, int sliceWidth, int sliceHeight, Ffv1Slice slice) {
    if (sliceWidth <= 0 || sliceHeight <= 0 || sliceX < 0 || sliceY < 0)
      throw new InvalidDataException($"A slice states invalid raster geometry ({sliceX},{sliceY}) {sliceWidth}x{sliceHeight}.");

    var endX = (long)sliceX + sliceWidth;
    var endY = (long)sliceY + sliceHeight;
    if (endX > parameters.HorizontalSlices || endY > parameters.VerticalSlices)
      throw new InvalidDataException(
        $"A slice states it covers columns {sliceX} to {endX - 1} and rows {sliceY} to {endY - 1} of a raster {parameters.HorizontalSlices} by {parameters.VerticalSlices}.");

    var x = (int)((long)sliceX * frameWidth / parameters.HorizontalSlices);
    var y = (int)((long)sliceY * frameHeight / parameters.VerticalSlices);
    var width = (int)(endX * frameWidth / parameters.HorizontalSlices) - x;
    var height = (int)(endY * frameHeight / parameters.VerticalSlices) - y;

    if (width <= 0 || height <= 0)
      throw new InvalidDataException($"A slice covers {width}x{height} pixels, which is not a picture.");

    return slice with { PixelX = x, PixelY = y, PixelWidth = width, PixelHeight = height };
  }

  private static void _MarkCoverage(
    Ffv1Parameters parameters, bool[] coverage, int sliceX, int sliceY, int sliceWidth, int sliceHeight) {
    for (var y = sliceY; y < sliceY + sliceHeight; ++y)
      for (var x = sliceX; x < sliceX + sliceWidth; ++x) {
        var index = checked(y * parameters.HorizontalSlices + x);
        if (coverage[index])
          throw new InvalidDataException($"Two FFV1 slices overlap at raster cell ({x},{y}).");

        coverage[index] = true;
      }
  }

  // ============================================================================================
  // Planes
  // ============================================================================================

  private static (int Width, int Height) _PlaneSize(Ffv1Parameters parameters, int plane, int width, int height) {
    if (parameters.ColourSpaceType == 1 || !parameters.ChromaPlanes || plane is not (1 or 2))
      return (width, height);

    var horizontal = 1 << parameters.ChromaHorizontalShift;
    var vertical = 1 << parameters.ChromaVerticalShift;
    return ((width + horizontal - 1) / horizontal, (height + vertical - 1) / vertical);
  }

  private Ffv1Plane[] _AllocatePlanes(Ffv1Parameters parameters) {
    var planes = new Ffv1Plane[parameters.PlaneCount];
    for (var plane = 0; plane < planes.Length; ++plane) {
      var (width, height) = _PlaneSize(parameters, plane, this._width, this._height);
      planes[plane] = new(width, height);
    }

    return planes;
  }

  private static void _Blit(Ffv1Parameters parameters, Ffv1Plane[] from, Ffv1Plane[] into, Ffv1Slice slice) {
    for (var plane = 0; plane < from.Length; ++plane) {
      var horizontal = parameters.ColourSpaceType == 0 && parameters.ChromaPlanes && plane is 1 or 2 ? parameters.ChromaHorizontalShift : 0;
      var vertical = parameters.ColourSpaceType == 0 && parameters.ChromaPlanes && plane is 1 or 2 ? parameters.ChromaVerticalShift : 0;
      var x0 = slice.PixelX >> horizontal;
      var y0 = slice.PixelY >> vertical;

      for (var y = 0; y < from[plane].Height; ++y) {
        var targetRow = y0 + y;
        if (targetRow >= into[plane].Height)
          break;

        for (var x = 0; x < from[plane].Width; ++x) {
          var targetColumn = x0 + x;
          if (targetColumn >= into[plane].Width)
            break;

          into[plane][targetColumn, targetRow] = from[plane][x, y];
        }
      }
    }
  }

  private static void _UndoColourTransform(Ffv1Parameters parameters, Ffv1Plane[] planes) {
    var offset = 1 << parameters.BitsPerRawSample;
    var mask = offset - 1;
    var first = planes[0];
    var second = planes[1];
    var third = planes[2];
    var oldRgbTransform = parameters.BitsPerRawSample is >= 9 and <= 15 && !parameters.ExtraPlane;

    for (var i = 0; i < first.Samples.Length; ++i) {
      var y = first.Samples[i];
      var c1 = second.Samples[i] - offset;
      var c2 = third.Samples[i] - offset;

      if (oldRgbTransform) {
        var blue = y - ((c1 + c2) >> 2);
        first.Samples[i] = (c1 + blue) & mask;  // green
        second.Samples[i] = blue & mask;
        third.Samples[i] = (c2 + blue) & mask; // red
      } else {
        var green = y - ((c1 + c2) >> 2);
        first.Samples[i] = green & mask;
        second.Samples[i] = (c1 + green) & mask; // blue
        third.Samples[i] = (c2 + green) & mask;  // red
      }
    }
  }

  // ============================================================================================
  // Adaptive states
  // ============================================================================================

  private void _PrepareStates(Ffv1Parameters parameters, int[] tableSetIndices, bool keyframe, int slice, int sliceCount) {
    if (parameters.CoderType == 0) {
      if (this._golombStates != null && this._golombStates.Length != sliceCount && !keyframe)
        throw new InvalidDataException("A non-key FFV1 frame changed its slice count, so its carried Golomb-Rice state no longer has a defined slice to belong to.");

      this._golombStates ??= new Ffv1GolombState[sliceCount][][];
      if (this._golombStates.Length != sliceCount)
        this._golombStates = new Ffv1GolombState[sliceCount][][];

      if (!keyframe && this._golombStates[slice] == null)
        throw new InvalidDataException($"FFV1 slice {slice} depends on Golomb-Rice state that has not been established by a keyframe.");

      if (keyframe || this._golombStates[slice] == null) {
        var kinds = new Ffv1GolombState[3][];
        for (var plane = 0; plane < parameters.PlaneCount; ++plane) {
          var kind = parameters.PlaneKindOf(plane);
          if (kinds[kind] != null)
            continue;

          var contexts = new Ffv1GolombState[parameters.ContextCount[tableSetIndices[kind]]];
          for (var context = 0; context < contexts.Length; ++context)
            contexts[context] = new();

          kinds[kind] = contexts;
        }

        this._golombStates[slice] = kinds;
      }

      return;
    }

    if (this._rangeStates != null && this._rangeStates.Length != sliceCount && !keyframe)
      throw new InvalidDataException("A non-key FFV1 frame changed its slice count, so its carried range-coder state no longer has a defined slice to belong to.");

    this._rangeStates ??= new byte[sliceCount][][][];
    if (this._rangeStates.Length != sliceCount)
      this._rangeStates = new byte[sliceCount][][][];

    if (!keyframe && this._rangeStates[slice] == null)
      throw new InvalidDataException($"FFV1 slice {slice} depends on range-coder state that has not been established by a keyframe.");

    if (!keyframe && this._rangeStates[slice] != null) {
      for (var plane = 0; plane < parameters.PlaneCount; ++plane) {
        var kind = parameters.PlaneKindOf(plane);
        var set = tableSetIndices[kind];
        if (this._rangeStates[slice][kind].Length != parameters.ContextCount[set])
          throw new InvalidDataException("A non-key FFV1 slice changed to a quantisation table with a different context count, so its carried range state cannot be applied safely.");
      }
      return;
    }

    var built = new byte[3][][];
    for (var plane = 0; plane < parameters.PlaneCount; ++plane) {
      var kind = parameters.PlaneKindOf(plane);
      if (built[kind] != null)
        continue;

      var set = tableSetIndices[kind];
      var contexts = new byte[parameters.ContextCount[set]][];
      for (var context = 0; context < contexts.Length; ++context) {
        var states = new byte[Ffv1RangeCoder.CONTEXT_SIZE];
        var stated = parameters.InitialStates?[set];
        if (stated != null && context < stated.Length)
          stated[context].CopyTo(states, 0);
        else
          Array.Fill(states, (byte)128);

        contexts[context] = states;
      }

      built[kind] = contexts;
    }

    this._rangeStates[slice] = built;
  }

  // ============================================================================================
  // Output
  // ============================================================================================

  private RawImage _Compose(Ffv1Parameters parameters, Ffv1Plane[] planes) {
    if (parameters.ColourSpaceType == 1)
      return this._FromColour(parameters, planes);

    return parameters.ChromaPlanes ? this._FromLuminance(parameters, planes) : this._FromGrey(parameters, planes);
  }

  private RawImage _FromGrey(Ffv1Parameters parameters, Ffv1Plane[] planes) {
    var count = checked(this._width * this._height);
    var luma = planes[0].Samples;

    if (parameters.BitsPerRawSample <= 8) {
      if (!parameters.ExtraPlane) {
        var grey = new byte[count];
        for (var i = 0; i < count; ++i)
          grey[i] = (byte)luma[i];

        return new() { Width = this._width, Height = this._height, Format = PixelFormat.Gray8, PixelData = grey };
      }

      var pixels = new byte[checked(count * 2)];
      var alpha = planes[1].Samples;
      for (var i = 0; i < count; ++i) {
        pixels[i * 2] = (byte)luma[i];
        pixels[i * 2 + 1] = (byte)alpha[i];
      }

      return new() { Width = this._width, Height = this._height, Format = PixelFormat.GrayAlpha16, PixelData = pixels };
    }

    var format = Ffv1SampleIO.GreyFormat(parameters.BitsPerRawSample, parameters.ExtraPlane);
    var channels = parameters.ExtraPlane ? 2 : 1;
    var deep = new byte[checked(count * channels * 2)];
    var mask = (1 << parameters.BitsPerRawSample) - 1;
    for (var i = 0; i < count; ++i) {
      Ffv1SampleIO.WriteSample(deep, i * channels, format, luma[i] & mask);
      if (channels == 2)
        Ffv1SampleIO.WriteSample(deep, i * 2 + 1, format, planes[1].Samples[i] & mask);
    }

    return new() { Width = this._width, Height = this._height, Format = format, PixelData = deep };
  }

  private RawImage _FromLuminance(Ffv1Parameters parameters, Ffv1Plane[] planes) {
    if (parameters.BitsPerRawSample > 8) {
      if (parameters.ExtraPlane)
        throw new NotSupportedException("This deep FFV1 stream carries planar YUV plus alpha, for which the raw-image model has no lossless planar YUVA representation.");

      var format = Ffv1SampleIO.YuvFormat(
        parameters.BitsPerRawSample, parameters.ChromaHorizontalShift, parameters.ChromaVerticalShift);
      var totalSamples = checked(planes[0].Samples.Length + planes[1].Samples.Length + planes[2].Samples.Length);
      var pixels = new byte[checked(totalSamples * 2)];
      var mask = (1 << parameters.BitsPerRawSample) - 1;
      var sample = 0;
      foreach (var plane in planes)
        foreach (var value in plane.Samples)
          Ffv1SampleIO.WriteSample(pixels, sample++, format, value & mask);

      return new() { Width = this._width, Height = this._height, Format = format, PixelData = pixels };
    }

    var luma = planes[0];
    var cb = planes[1];
    var cr = planes[2];
    var alpha = parameters.ExtraPlane ? planes[3] : null;
    var channels = alpha == null ? 3 : 4;
    var pixels8 = new byte[checked(this._width * this._height * channels)];

    for (var y = 0; y < this._height; ++y) {
      var chromaRow = Math.Min(y >> parameters.ChromaVerticalShift, cb.Height - 1);
      var target = checked(y * this._width * channels);

      for (var x = 0; x < this._width; ++x) {
        var chromaColumn = Math.Min(x >> parameters.ChromaHorizontalShift, cb.Width - 1);

        var scaledLuma = 298 * (luma[x, y] - 16);
        var blueDifference = cb[chromaColumn, chromaRow] - 128;
        var redDifference = cr[chromaColumn, chromaRow] - 128;

        pixels8[target] = _Clamp(scaledLuma + 409 * redDifference + 128);
        pixels8[target + 1] = _Clamp(scaledLuma - 100 * blueDifference - 208 * redDifference + 128);
        pixels8[target + 2] = _Clamp(scaledLuma + 516 * blueDifference + 128);
        if (alpha != null)
          pixels8[target + 3] = (byte)alpha[x, y];

        target += channels;
      }
    }

    return new() {
      Width = this._width,
      Height = this._height,
      Format = channels == 3 ? PixelFormat.Rgb24 : PixelFormat.Rgba32,
      PixelData = pixels8,
    };
  }

  private RawImage _FromColour(Ffv1Parameters parameters, Ffv1Plane[] planes) {
    var count = checked(this._width * this._height);
    var green = planes[0].Samples;
    var blue = planes[1].Samples;
    var red = planes[2].Samples;
    var channels = parameters.ExtraPlane ? 4 : 3;

    if (parameters.BitsPerRawSample <= 8) {
      var pixels = new byte[checked(count * channels)];
      for (var i = 0; i < count; ++i) {
        pixels[i * channels] = (byte)red[i];
        pixels[i * channels + 1] = (byte)green[i];
        pixels[i * channels + 2] = (byte)blue[i];
        if (channels == 4)
          pixels[i * 4 + 3] = (byte)planes[3].Samples[i];
      }

      return new() {
        Width = this._width,
        Height = this._height,
        Format = channels == 3 ? PixelFormat.Rgb24 : PixelFormat.Rgba32,
        PixelData = pixels,
      };
    }

    var format = Ffv1SampleIO.RgbFormat(parameters.BitsPerRawSample, parameters.ExtraPlane);
    var deep = new byte[checked(count * channels * 2)];
    var mask = (1 << parameters.BitsPerRawSample) - 1;
    for (var i = 0; i < count; ++i) {
      Ffv1SampleIO.WriteSample(deep, i * channels, format, red[i] & mask);
      Ffv1SampleIO.WriteSample(deep, i * channels + 1, format, green[i] & mask);
      Ffv1SampleIO.WriteSample(deep, i * channels + 2, format, blue[i] & mask);
      if (channels == 4)
        Ffv1SampleIO.WriteSample(deep, i * 4 + 3, format, planes[3].Samples[i] & mask);
    }

    return new() { Width = this._width, Height = this._height, Format = format, PixelData = deep };
  }

  private static byte _Clamp(int scaled) {
    var value = scaled >> 8;
    return (byte)(value < 0 ? 0 : value > 255 ? 255 : value);
  }

  private static void _RefuseUnread(Ffv1Parameters parameters, int streamIndex) {
    if (parameters.BitsPerRawSample is < 8 or > 16)
      throw new NotSupportedException(
        $"Video stream {streamIndex} carries {parameters.BitsPerRawSample}-bit samples. FFV1 sample widths from eight through sixteen bits are read here.");

    if (parameters.Version >= 3) {
      if (parameters.HorizontalSlices <= 0 || parameters.VerticalSlices <= 0
          || parameters.HorizontalSlices > _MAX_SLICES / parameters.VerticalSlices)
        throw new NotSupportedException(
          $"Video stream {streamIndex} describes a {parameters.HorizontalSlices}x{parameters.VerticalSlices} FFV1 slice raster, exceeding the {_MAX_SLICES}-slice interoperability limit.");
    }

    if (parameters.ChromaHorizontalShift is < 0 or > 8 || parameters.ChromaVerticalShift is < 0 or > 8)
      throw new NotSupportedException(
        $"Video stream {streamIndex} uses chroma shifts {parameters.ChromaHorizontalShift},{parameters.ChromaVerticalShift}, outside the supported 0..8 range.");

    if (parameters.BitsPerRawSample > 8 && parameters.ColourSpaceType == 0 && parameters.ChromaPlanes
        && (parameters.ChromaHorizontalShift > 1 || parameters.ChromaVerticalShift > 1))
      throw new NotSupportedException(
        $"Video stream {streamIndex} uses deep planar YUV with chroma shifts {parameters.ChromaHorizontalShift},{parameters.ChromaVerticalShift}; the raw-image model has exact deep YUV layouts only for 4:4:4, 4:4:0, 4:2:2 and 4:2:0.");
  }

  private static byte[] _FreshStates() {
    var states = new byte[Ffv1RangeCoder.CONTEXT_SIZE];
    Array.Fill(states, (byte)128);
    return states;
  }
}