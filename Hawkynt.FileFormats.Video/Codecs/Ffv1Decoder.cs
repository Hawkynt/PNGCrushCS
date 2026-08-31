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
/// <b>Two entropy coders, one everything else.</b> The range coder (<see cref="Ffv1RangeCoder"/>)
/// spends thirty-two adaptive states on each context; Golomb-Rice
/// (<see cref="Ffv1GolombDecoder"/>) spends four running numbers and adds a run mode for the flat
/// areas. The prediction, the contexts and the plane order are the same either way.
/// <para/>
/// <b>Where the header lives is the version.</b> Versions 0 and 1 put it inside every keyframe;
/// version 3 moves it into a configuration record the container carries, adds slices that can be
/// found and decoded independently of one another, and protects both with a checksum. Version 2 was
/// never finished and is refused by name.
/// <para/>
/// <b>Measured against ffmpeg.</b> Every pixel format its encoder writes at eight bits, in both
/// coders, at versions 0, 1 and 3, with one slice and with four, with and without slice checksums,
/// with the range coder's own state transition table and with the default one. The formats that need
/// no colour conversion are compared against ffmpeg's own frames and are identical; the
/// luminance-and-chrominance ones are compared plane by plane against ffmpeg's decoded planes and
/// every sample of every plane is identical.
/// <para/>
/// <b>What refuses.</b> Samples deeper than eight bits, version 2, a coder type or colour space the
/// specification does not describe, a slice whose checksum does not come out, a slice raster with a
/// hole in it. There is no <c>catch</c> here handing back a blank or a repeated frame.
/// </remarks>
public sealed class Ffv1Decoder : IVideoCodecDecoder<Ffv1Decoder> {

  /// <summary>The four-character code containers name this codec with.</summary>
  private static readonly CodecTag _FFV1 = CodecTag.FromCharacters("FFV1");

  /// <summary>What Matroska calls it, which is a name rather than a code.</summary>
  private const string _MATROSKA_CODEC_ID = "V_FFV1";

  /// <summary>How much of a slice's tail is its footer, without and with a checksum.</summary>
  private const int _FOOTER_LENGTH = 3;
  private const int _FOOTER_LENGTH_WITH_CHECKSUM = 8;

  private readonly int _width;
  private readonly int _height;
  private readonly Ffv1Parameters? _configured;

  /// <summary>
  /// What the last keyframe of a version 0 or 1 stream said about itself.
  /// </summary>
  /// <remarks>
  /// Those versions state their parameters in keyframes only, so a frame that is not one is decoded
  /// against the last keyframe's description. A stream that opens with one is refused rather than
  /// decoded against a description invented for it.
  /// </remarks>
  private Ffv1Parameters? _stated;

  /// <summary>
  /// The sample-coding states, one set per slice and plane, which a frame that is not a keyframe
  /// carries on from.
  /// </summary>
  /// <remarks>
  /// Per slice and per plane rather than per quantisation table set, which is the thing about them
  /// easiest to get wrong. Two planes sharing a table set share how many contexts they have and what
  /// puts a sample in which of them; they do not share what those contexts have learned, and a
  /// decoder that let them does not go wrong on a greyscale stream at all and goes wrong on every
  /// sample of a colour one.
  /// </remarks>
  private byte[][][][]? _rangeStates;

  private Ffv1GolombState[][][]? _golombStates;

  private Ffv1Decoder(int width, int height, Ffv1Parameters? configured) {
    this._width = width;
    this._height = height;
    this._configured = configured;
  }

  /// <summary>Gets the codec name.</summary>
  public static string CodecName => "FFV1 (RFC 9043)";

  /// <summary>Determines whether the specified media stream is supported.</summary>
  public static bool Accepts(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);

    return stream.Kind == MediaStreamKind.Video
           && (stream.Codec.EqualsIgnoringCase(_FFV1)
               || string.Equals(stream.CodecId, _MATROSKA_CODEC_ID, StringComparison.OrdinalIgnoreCase));
  }

  /// <summary>
  /// Builds a decoder, reading the configuration record where the stream is one that has one.
  /// </summary>
  /// <remarks>
  /// A version 3 stream cannot be decoded without it, so it is read here and a stream missing it is
  /// refused before a frame arrives rather than part way into one. A version 0 or 1 stream carries
  /// its parameters in every keyframe and needs nothing from the container at all — which is why the
  /// record is optional here rather than required.
  /// </remarks>
  public static Ffv1Decoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);

    if (stream.Width <= 0 || stream.Height <= 0)
      throw new InvalidDataException(
        $"Video stream {stream.Index} states a picture size of {stream.Width}x{stream.Height}, which no frame can be decoded into.");

    var record = _ConfigurationRecord(stream);
    return new(stream.Width, stream.Height, record.IsEmpty ? null : _ReadConfigurationRecord(record, stream.Index));
  }

  /// <summary>
  /// The configuration record out of the stream description, whichever container carried it.
  /// </summary>
  /// <remarks>
  /// A Matroska track's private data is the record and nothing else. An AVI's stream format is a
  /// <c>BITMAPINFOHEADER</c> with the record appended, so the record is what follows it. A stream
  /// whose description is only the header carries no record and is a version 0 or 1 stream.
  /// </remarks>
  private static ReadOnlyMemory<byte> _ConfigurationRecord(MediaStreamInfo stream) {
    var description = stream.CodecPrivateData;
    if (description.IsEmpty)
      return ReadOnlyMemory<byte>.Empty;

    if (string.Equals(stream.CodecId, _MATROSKA_CODEC_ID, StringComparison.OrdinalIgnoreCase))
      return description;

    return description.Length > BitmapInfoHeader.StructSize ? description[BitmapInfoHeader.StructSize..] : ReadOnlyMemory<byte>.Empty;
  }

  /// <summary>Reads the record, checking the four bytes of parity at the end of it first.</summary>
  private static Ffv1Parameters _ReadConfigurationRecord(ReadOnlyMemory<byte> record, int streamIndex) {
    if (record.Length < 5)
      throw new InvalidDataException(
        $"Video stream {streamIndex} carries {record.Length} bytes of configuration record, which is shorter than the four bytes of checksum at the end of one.");

    if (Ffv1Crc.Of(record.Span) != 0)
      throw new InvalidDataException(
        $"Video stream {streamIndex} carries a configuration record whose checksum does not come out, so what it says about the stream cannot be trusted.");

    var states = new byte[Ffv1RangeCoder.CONTEXT_SIZE];
    Array.Fill(states, (byte)128);

    var (zero, one) = Ffv1StateTransition.Build([]);
    var coder = new Ffv1RangeCoder(record[..^4], zero, one);
    var parameters = Ffv1Parameters.Read(coder, states, true);

    _RefuseUnread(parameters, streamIndex);
    return parameters;
  }

  /// <summary>
  /// Turns one packet into the picture it codes.
  /// </summary>
  /// <remarks>
  /// Every packet is a whole frame; FFV1 has no reordering and nothing is ever held back. A frame
  /// that is not a keyframe still codes every sample of the picture — what it inherits from the
  /// frame before it is the entropy coder's statistics and not any part of the image.
  /// </remarks>
  public bool TryDecode(CodedPacket packet, out RawImage frame) {
    var data = packet.Data;
    if (data.IsEmpty)
      throw new InvalidDataException("A frame of no bytes cannot be decoded, and a repeat of the frame before it is not what a frame of no bytes means.");

    var (zero, one) = Ffv1StateTransition.Build(
      this._configured is { HasStateTransitionDelta: true } ? this._configured.StateTransitionDelta : []);

    // The bit that says whether this is a keyframe has a state of its own that starts at 128 for
    // every frame. It is not one of the states a keyframe resets and a later frame carries on from:
    // those are the sample-coding ones, and this bit is read before a frame has said which it is.
    var keyframeState = new byte[Ffv1RangeCoder.CONTEXT_SIZE];
    Array.Fill(keyframeState, (byte)128);

    var coder = new Ffv1RangeCoder(data, zero, one);
    var keyframe = coder.Get(keyframeState, 0) != 0;

    var parameters = this._configured;
    if (parameters == null) {
      if (keyframe) {
        var headerStates = new byte[Ffv1RangeCoder.CONTEXT_SIZE];
        Array.Fill(headerStates, (byte)128);
        this._stated = Ffv1Parameters.Read(coder, headerStates, false);
        _RefuseUnread(this._stated, 0);
      }

      parameters = this._stated
                   ?? throw new InvalidDataException(
                     "The stream opens with a frame that is not a keyframe, and a version 0 or 1 stream states how it is coded only in its keyframes.");

      if (parameters.HasStateTransitionDelta) {
        // The header itself was read with the default table, which is what the specification means
        // by the differences being part of the parameters: they take effect for the samples that
        // follow and not for the field that stated them.
        (zero, one) = Ffv1StateTransition.Build(parameters.StateTransitionDelta);
        coder.UseStateTransitions(zero, one);
      }
    }

    frame = this._DecodeFrame(parameters, data, coder, keyframe, zero, one);
    return true;
  }

  // ============================================================================================
  // The frame
  // ============================================================================================

  private RawImage _DecodeFrame(
    Ffv1Parameters parameters, ReadOnlyMemory<byte> data, Ffv1RangeCoder frameCoder, bool keyframe, byte[] zero, byte[] one) {
    var planes = this._AllocatePlanes(parameters);
    var slices = _SlicePositions(parameters, this._width, this._height, data);

    for (var index = 0; index < slices.Count; ++index) {
      var slice = slices[index];
      var body = data.Slice(slice.Offset, slice.Length);

      // The first slice carries on with the coder that read the frame's keyframe bit, because it
      // begins at the same byte the frame does. Every later one begins where the slice before it
      // ended and gets a coder of its own, which is what makes them independent of each other.
      var coder = index == 0 ? frameCoder : new Ffv1RangeCoder(body, zero, one);

      this._DecodeSlice(parameters, coder, body, slice, planes, keyframe, index, slices.Count);
    }

    return this._Compose(parameters, planes);
  }

  private void _DecodeSlice(
    Ffv1Parameters parameters, Ffv1RangeCoder coder, ReadOnlyMemory<byte> body, Ffv1Slice slice, Ffv1Plane[] planes,
    bool keyframe, int index, int sliceCount) {
    var tableSetIndices = new int[3];
    var geometry = slice;

    if (parameters.Version >= 3) {
      var headerStates = new byte[Ffv1RangeCoder.CONTEXT_SIZE];
      Array.Fill(headerStates, (byte)128);

      var sliceX = coder.Symbol(headerStates, false);
      var sliceY = coder.Symbol(headerStates, false);
      var sliceWidth = coder.Symbol(headerStates, false) + 1;
      var sliceHeight = coder.Symbol(headerStates, false) + 1;

      for (var i = 0; i < parameters.QuantTableSetIndexCount; ++i) {
        var stated = coder.Symbol(headerStates, false);
        if (stated >= parameters.QuantTableSetCount)
          throw new InvalidDataException($"A slice names quantisation table set {stated}, where the stream states {parameters.QuantTableSetCount}.");

        if (i < tableSetIndices.Length)
          tableSetIndices[i] = stated;
      }

      coder.Symbol(headerStates, false);   // picture structure
      coder.Symbol(headerStates, false);   // sample aspect ratio numerator
      coder.Symbol(headerStates, false);   // and denominator

      geometry = _GeometryOf(parameters, this._width, this._height, sliceX, sliceY, sliceWidth, sliceHeight, slice);
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
      // Plane and then line: each plane is finished before the next begins.
      for (var plane = 0; plane < slicePlanes.Length; ++plane) {
        var tableSet = parameters.TableSetIndexOf(plane, tableSetIndices);
        runIndex = 0;
        if (golomb == null)
          decoder.DecodePlane(coder, slicePlanes[plane], this._rangeStates![index][parameters.PlaneKindOf(plane)], tableSet);
        else
          decoder.DecodePlane(golomb, slicePlanes[plane], this._golombStates![index][parameters.PlaneKindOf(plane)], tableSet, ref runIndex);
      }
    } else {
      // Line and then plane, because the colour transform is undone a line at a time and reading the
      // three planes together is what keeps that line in cache.
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

  /// <summary>
  /// Starts the plain bit reader a Golomb-coded slice's samples are in.
  /// </summary>
  /// <remarks>
  /// A slice's header is range coded whichever coder its samples use, and a range coder reads ahead
  /// of itself — so where the bits begin is not where the header ended. The specification's answer
  /// is a symbol coded against a fixed state and thrown away, after which exactly one byte past the
  /// coded data has been read; the bits start at the byte before that.
  /// </remarks>
  private Ffv1GolombDecoder _StartGolomb(Ffv1Parameters parameters, Ffv1RangeCoder coder, ReadOnlyMemory<byte> body) {
    if (parameters.Version >= 3 && parameters.MicroVersion > 1)
      coder.ReadTerminator();

    return new(body, Math.Max(0, coder.BytesRead - 1));
  }

  // ============================================================================================
  // Slices
  // ============================================================================================

  private readonly record struct Ffv1Slice(int Offset, int Length, int PixelX, int PixelY, int PixelWidth, int PixelHeight);

  /// <summary>
  /// Where each slice of a frame begins and ends.
  /// </summary>
  /// <remarks>
  /// Found from the back. Every slice of a version 3 frame ends with its own length, so the last
  /// slice's footer is at the end of the frame and each earlier one is found by stepping back over
  /// the slice after it. That is what lets slices be decoded in any order or in parallel, and what
  /// lets a damaged one be skipped instead of taking the frame with it.
  /// <para/>
  /// A version 0 or 1 frame has no footers and no slice headers: it is one slice covering the whole
  /// picture, and it runs to the end of the packet.
  /// </remarks>
  private static List<Ffv1Slice> _SlicePositions(Ffv1Parameters parameters, int width, int height, ReadOnlyMemory<byte> data) {
    if (parameters.Version <= 1)
      return [new(0, data.Length, 0, 0, width, height)];

    var footer = parameters.ErrorCorrection != 0 ? _FOOTER_LENGTH_WITH_CHECKSUM : _FOOTER_LENGTH;
    var count = parameters.HorizontalSlices * parameters.VerticalSlices;
    var slices = new List<Ffv1Slice>(count);
    var end = data.Length;

    for (var i = 0; i < count; ++i) {
      if (end < footer)
        throw new InvalidDataException($"A frame of {data.Length} bytes ends before the {count} slice(s) it states do.");

      var span = data.Span;
      var stated = (span[end - footer] << 16) | (span[end - footer + 1] << 8) | span[end - footer + 2];
      var length = stated + footer;
      if (length <= 0 || length > end)
        throw new InvalidDataException($"A slice states a length of {stated} bytes where {end - footer} are left in front of it.");

      var offset = end - length;
      if (parameters.ErrorCorrection != 0 && Ffv1Crc.Of(data.Span.Slice(offset, length)) != 0)
        throw new InvalidDataException($"A slice of {length} bytes has a checksum that does not come out, so the picture it holds is damaged.");

      slices.Add(new(offset, length, 0, 0, 0, 0));
      end = offset;
    }

    slices.Reverse();
    return slices;
  }

  /// <summary>Turns a slice's place in the raster into the pixels it covers (RFC 9043 §4.7).</summary>
  private static Ffv1Slice _GeometryOf(
    Ffv1Parameters parameters, int frameWidth, int frameHeight, int sliceX, int sliceY, int sliceWidth, int sliceHeight, Ffv1Slice slice) {
    if (sliceX + sliceWidth > parameters.HorizontalSlices || sliceY + sliceHeight > parameters.VerticalSlices)
      throw new InvalidDataException(
        $"A slice states it covers columns {sliceX} to {sliceX + sliceWidth - 1} and rows {sliceY} to {sliceY + sliceHeight - 1} of a raster {parameters.HorizontalSlices} by {parameters.VerticalSlices}.");

    var x = (int)((long)sliceX * frameWidth / parameters.HorizontalSlices);
    var y = (int)((long)sliceY * frameHeight / parameters.VerticalSlices);
    var width = (int)((long)(sliceX + sliceWidth) * frameWidth / parameters.HorizontalSlices) - x;
    var height = (int)((long)(sliceY + sliceHeight) * frameHeight / parameters.VerticalSlices) - y;

    if (width <= 0 || height <= 0)
      throw new InvalidDataException($"A slice covers {width}x{height} pixels, which is not a picture.");

    return slice with { PixelX = x, PixelY = y, PixelWidth = width, PixelHeight = height };
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

  /// <summary>
  /// Undoes the JPEG 2000 reversible colour transform (RFC 9043 §3.7.2).
  /// </summary>
  /// <remarks>
  /// Reversible because it is integer arithmetic that loses nothing: green is recovered by taking a
  /// quarter of the two colour differences back off the luminance, and red and blue by adding green
  /// to them. The shift is arithmetic and rounds downwards, which is what makes it the exact inverse
  /// of the shift the encoder used and not merely close to it.
  /// </remarks>
  private static void _UndoColourTransform(Ffv1Parameters parameters, Ffv1Plane[] planes) {
    var offset = 1 << parameters.BitsPerRawSample;
    var mask = (1 << parameters.BitsPerRawSample) - 1;
    var luma = planes[0];
    var cb = planes[1];
    var cr = planes[2];

    for (var i = 0; i < luma.Samples.Length; ++i) {
      var y = luma.Samples[i];
      var b = cb.Samples[i] - offset;
      var r = cr.Samples[i] - offset;

      var green = y - ((b + r) >> 2);
      luma.Samples[i] = green & mask;
      cb.Samples[i] = (b + green) & mask;
      cr.Samples[i] = (r + green) & mask;
    }
  }

  // ============================================================================================
  // The states
  // ============================================================================================

  /// <summary>
  /// Puts the entropy coder's statistics where the frame expects to find them.
  /// </summary>
  /// <remarks>
  /// A keyframe resets them; a frame that is not one carries on from where the frame before it left
  /// off. That is the only thing a frame inherits from its predecessor — every sample is still coded
  /// — and it is also why a stream cannot be entered part way through unless it says every frame is
  /// a keyframe.
  /// </remarks>
  private void _PrepareStates(Ffv1Parameters parameters, int[] tableSetIndices, bool keyframe, int slice, int sliceCount) {
    if (parameters.CoderType == 0) {
      this._golombStates ??= new Ffv1GolombState[sliceCount][][];
      if (this._golombStates.Length != sliceCount)
        this._golombStates = new Ffv1GolombState[sliceCount][][];

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

    this._rangeStates ??= new byte[sliceCount][][][];
    if (this._rangeStates.Length != sliceCount)
      this._rangeStates = new byte[sliceCount][][][];

    if (!keyframe && this._rangeStates[slice] != null)
      return;

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
  // What comes out
  // ============================================================================================

  private RawImage _Compose(Ffv1Parameters parameters, Ffv1Plane[] planes) {
    if (parameters.ColourSpaceType == 1)
      return this._FromColour(parameters, planes);

    return parameters.ChromaPlanes ? this._FromLuminance(parameters, planes) : this._FromGrey(parameters, planes);
  }

  private RawImage _FromGrey(Ffv1Parameters parameters, Ffv1Plane[] planes) {
    var count = this._width * this._height;
    var luma = planes[0].Samples;

    if (!parameters.ExtraPlane) {
      var grey = new byte[count];
      for (var i = 0; i < count; ++i)
        grey[i] = (byte)luma[i];

      return new() { Width = this._width, Height = this._height, Format = PixelFormat.Gray8, PixelData = grey };
    }

    var pixels = new byte[count * 2];
    var alpha = planes[1].Samples;
    for (var i = 0; i < count; ++i) {
      pixels[i * 2] = (byte)luma[i];
      pixels[i * 2 + 1] = (byte)alpha[i];
    }

    return new() { Width = this._width, Height = this._height, Format = PixelFormat.GrayAlpha16, PixelData = pixels };
  }

  /// <summary>
  /// Turns luminance and chrominance into the packed colour every reader here hands back.
  /// </summary>
  /// <remarks>
  /// The conversion is a display convention rather than part of the coding: FFV1 codes samples and
  /// says nothing about what to do with them. ITU-R BT.601 with studio swing, and each chrominance
  /// sample repeated across the block it covers.
  /// </remarks>
  private RawImage _FromLuminance(Ffv1Parameters parameters, Ffv1Plane[] planes) {
    var luma = planes[0];
    var cb = planes[1];
    var cr = planes[2];
    var alpha = parameters.ExtraPlane ? planes[3] : null;
    var channels = alpha == null ? 3 : 4;
    var pixels = new byte[this._width * this._height * channels];

    for (var y = 0; y < this._height; ++y) {
      var chromaRow = Math.Min(y >> parameters.ChromaVerticalShift, cb.Height - 1);
      var target = y * this._width * channels;

      for (var x = 0; x < this._width; ++x) {
        var chromaColumn = Math.Min(x >> parameters.ChromaHorizontalShift, cb.Width - 1);

        var scaledLuma = 298 * (luma[x, y] - 16);
        var blueDifference = cb[chromaColumn, chromaRow] - 128;
        var redDifference = cr[chromaColumn, chromaRow] - 128;

        pixels[target] = _Clamp(scaledLuma + 409 * redDifference + 128);
        pixels[target + 1] = _Clamp(scaledLuma - 100 * blueDifference - 208 * redDifference + 128);
        pixels[target + 2] = _Clamp(scaledLuma + 516 * blueDifference + 128);
        if (alpha != null)
          pixels[target + 3] = (byte)alpha[x, y];

        target += channels;
      }
    }

    return new() {
      Width = this._width,
      Height = this._height,
      Format = channels == 3 ? PixelFormat.Rgb24 : PixelFormat.Rgba32,
      PixelData = pixels,
    };
  }

  /// <summary>The three transformed planes, which came back as green, blue and red.</summary>
  private RawImage _FromColour(Ffv1Parameters parameters, Ffv1Plane[] planes) {
    var count = this._width * this._height;
    var green = planes[0].Samples;
    var blue = planes[1].Samples;
    var red = planes[2].Samples;

    if (parameters.ExtraPlane) {
      var alpha = planes[3].Samples;
      var rgba = new byte[count * 4];
      for (var i = 0; i < count; ++i) {
        rgba[i * 4] = (byte)red[i];
        rgba[i * 4 + 1] = (byte)green[i];
        rgba[i * 4 + 2] = (byte)blue[i];
        rgba[i * 4 + 3] = (byte)alpha[i];
      }

      return new() { Width = this._width, Height = this._height, Format = PixelFormat.Rgba32, PixelData = rgba };
    }

    var rgb = new byte[count * 3];
    for (var i = 0; i < count; ++i) {
      rgb[i * 3] = (byte)red[i];
      rgb[i * 3 + 1] = (byte)green[i];
      rgb[i * 3 + 2] = (byte)blue[i];
    }

    return new() { Width = this._width, Height = this._height, Format = PixelFormat.Rgb24, PixelData = rgb };
  }

  private static byte _Clamp(int scaled) {
    var value = scaled >> 8;
    return (byte)(value < 0 ? 0 : value > 255 ? 255 : value);
  }

  /// <summary>Refuses what the specification describes but this does not read.</summary>
  private static void _RefuseUnread(Ffv1Parameters parameters, int streamIndex) {
    if (parameters.BitsPerRawSample != 8)
      throw new NotSupportedException(
        $"Video stream {streamIndex} carries {parameters.BitsPerRawSample}-bit samples. Only eight-bit FFV1 is read here — the deeper samplings change the width of every coded difference and nothing here has been measured against one.");
  }
}
