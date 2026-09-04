using System;
using System.IO;
using FileFormat.Codecs.MagicYuv;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>
/// Encodes MagicYUV: a spatial prediction, its differences Huffman coded with one table a plane, and
/// the frame cut into slices that decode independently of one another.
/// </summary>
/// <remarks>
/// Adapted from FFmpeg's <c>libavcodec/magicyuvenc.c</c>, copyright (c) 2017 Paul B Mahol,
/// LGPL-2.1-or-later; this adaptation is distributed with PNGCrushCS under LGPL-3.0-or-later.
/// <para/>
/// <b>What it writes.</b> Eight-bit progressive frames in the six layouts that have a picture type to
/// come from: grey (<c>M8G0</c>), colour with and without alpha (<c>M8RG</c>, <c>M8RA</c>), and
/// planar luminance and chrominance at 4:4:4, 4:2:2 and 4:2:0 (<c>M8Y4</c>, <c>M8Y2</c>,
/// <c>M8Y0</c>). Every frame is a key frame, because the codec has no other kind. The header is the
/// 32-byte one the package's decoder measures real frames against, byte for byte what ffmpeg's
/// encoder writes; the offsets, the slice map, the tables and the slices follow it in that order.
/// <para/>
/// <b>What it refuses.</b> Luminance with alpha (<c>M8YA</c>), because no picture type here carries
/// planar chrominance beside an alpha plane; grey with alpha (<c>M8GA</c>), the codec's original
/// undifferentiated code (<c>MAGY</c>) and every deeper-than-eight-bit code, because the package's
/// own decoder refuses them and a frame it cannot read back is not one worth writing. A picture of
/// the wrong size, or with too few bytes for its size, is refused rather than padded.
/// <para/>
/// <b>Prediction and slices are chosen when the encoder is built.</b> The three predictors are the
/// format's own; the median is the default because it is the one the codec's author uses by default
/// and the one that compresses best on nearly everything. The slice count is a request — it is
/// clamped to the rows available, aligned so a 4:2:0 frame is never cut between the two luminance
/// rows that share a chrominance row, and limited by the eight-bit piece ids of the slice map.
/// One slice is the default: the tables are per plane and shared by every slice, so cutting finer
/// buys nothing but parallel decoding.
/// <para/>
/// <b>A slice that would not compress is stored plainly</b>, with its first byte saying so, exactly
/// as ffmpeg decides it: whenever the coded bytes would be no fewer than the samples themselves.
/// <para/>
/// <b>The picture is converted to the layout the stream names</b> where it does not already have
/// it, with the package's own converter. For the colour and grey layouts that is lossless whenever
/// the source has no more channels than the target; for the luminance layouts it is the converter's
/// colour transform, and what this encoder then promises is that the samples it was given after
/// that transform come back exactly, not the pixels before it.
/// </remarks>
public sealed class MagicYuvEncoder : IVideoCodecEncoder<MagicYuvEncoder> {

  /// <summary>The three ways a slice may predict a sample, as the format numbers them.</summary>
  public enum Predictor {

    /// <summary>The sample to the left.</summary>
    Left = 1,

    /// <summary>Left plus above less above-left.</summary>
    Gradient = 2,

    /// <summary>The median of the left, the above, and the gradient of the two.</summary>
    Median = 3,
  }

  /// <summary>The longest code a table may use, which is what every frame states and what real frames use.</summary>
  private const int _LONGEST_CODE = 12;

  /// <summary>The two values a slice's first byte takes.</summary>
  private const byte _CODED = 0;
  private const byte _UNCOMPRESSED = 1;

  /// <summary>The byte at offset 14 of the header, which is 0x20 in every frame ffmpeg writes.</summary>
  private const byte _CODER_TYPE = 0x20;

  private static readonly CodecTag _DefaultTag = CodecTag.FromCharacters("M8RG");

  private readonly MediaStreamInfo _stream;
  private readonly MagicYuvFormat _format;
  private readonly byte _formatByte;
  private readonly PixelFormat _pixelFormat;
  private readonly MagicYuvPredictor _predictor;
  private readonly int _sliceHeight;
  private readonly int _sliceCount;

  private MagicYuvEncoder(MediaStreamInfo stream, CodecTag tag, byte formatByte, PixelFormat pixelFormat, Predictor predictor, int slices) {
    this._format = MagicYuvFormat.Of(tag, stream.Index);
    this._formatByte = formatByte;
    this._pixelFormat = pixelFormat;
    this._predictor = (MagicYuvPredictor)predictor;

    // ffmpeg's arithmetic: as many slices as asked for but never more than the chrominance rows,
    // never more than the map can name, the height rounded up to whole chrominance rows
    var verticalShift = this._format.ChromaVerticalShift;
    var align = 1 << verticalShift;
    var most = Math.Min(Math.Max(1, stream.Height >> verticalShift), 256 / this._format.PlaneCount);
    var wanted = Math.Min(slices, most);
    var sliceHeight = (stream.Height + wanted - 1) / wanted;
    sliceHeight = (sliceHeight + align - 1) & ~(align - 1);
    this._sliceHeight = sliceHeight;
    this._sliceCount = (stream.Height + sliceHeight - 1) / sliceHeight;

    this._stream = new() {
      Index = stream.Index,
      Kind = MediaStreamKind.Video,
      Codec = tag,
      Handler = tag,
      CodecId = "magicyuv",
      TimeBase = stream.TimeBase,
      FrameRate = stream.FrameRate,
      DeclaredFrameCount = stream.DeclaredFrameCount,
      Width = stream.Width,
      Height = stream.Height,
      BitsPerPixel = RawImage.BitsPerPixel(pixelFormat),
      Language = stream.Language,
      Name = stream.Name,
    };
  }

  /// <summary>The codec's name as a person would say it.</summary>
  public static string CodecName => "MagicYUV";

  /// <summary>The code written when the stream names none of the codec's own: colour without alpha.</summary>
  public static CodecTag Codec => _DefaultTag;

  /// <summary>
  /// Builds an encoder for the stream described, predicting by median and writing one slice a frame.
  /// </summary>
  /// <remarks>
  /// The stream's code picks the layout where it is one of the codec's own. Where it is not — a
  /// stream fresh from another codec, or one with no code at all — the layout follows the depth the
  /// stream states: eight bits is grey, thirty-two is colour with alpha, anything else colour without.
  /// </remarks>
  public static MagicYuvEncoder Create(MediaStreamInfo stream) => Create(stream, Predictor.Median, 1);

  /// <summary>
  /// Builds an encoder for the stream described, with the prediction and the slice count stated.
  /// </summary>
  /// <param name="stream">The stream to encode; see <see cref="Create(MediaStreamInfo)"/> for how its code is read.</param>
  /// <param name="predictor">Which of the format's three predictors every slice uses.</param>
  /// <param name="slices">How many slices to cut each frame into, at least one; clamped to what the picture and the format allow.</param>
  public static MagicYuvEncoder Create(MediaStreamInfo stream, Predictor predictor, int slices) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.Kind != MediaStreamKind.Video)
      throw new NotSupportedException("MagicYUV can only encode a video stream.");

    if (stream.Width <= 0 || stream.Height <= 0)
      throw new NotSupportedException(
        $"A MagicYUV encoder needs the picture size before the first frame; {stream.Width}x{stream.Height} was stated.");

    if (predictor is not (Predictor.Left or Predictor.Gradient or Predictor.Median))
      throw new ArgumentOutOfRangeException(nameof(predictor), predictor, "MagicYUV has exactly three predictors.");

    if (slices < 1)
      throw new ArgumentOutOfRangeException(nameof(slices), slices, "A frame has at least one slice.");

    var name = stream.Codec.ToString();
    var (tag, formatByte, pixelFormat) = name switch {
      "M8RG" => ("M8RG", (byte)0x65, PixelFormat.Rgb24),
      "M8RA" => ("M8RA", (byte)0x66, PixelFormat.Rgba32),
      "M8Y4" => ("M8Y4", (byte)0x67, PixelFormat.Yuv444P8),
      "M8Y2" => ("M8Y2", (byte)0x68, PixelFormat.Yuv422P8),
      "M8Y0" => ("M8Y0", (byte)0x69, PixelFormat.Yuv420P8),
      "M8G0" => ("M8G0", (byte)0x6B, PixelFormat.Gray8),
      "M8YA" => throw new NotSupportedException(
        $"Video stream {stream.Index} asks for M8YA — luminance and chrominance with an alpha plane. No picture type here carries planar chrominance beside alpha, so there is nothing to write it from, and it is refused rather than written with an alpha plane invented for it."),
      "M8GA" => throw new NotSupportedException(
        $"Video stream {stream.Index} asks for M8GA — grey with an alpha channel — which the package's own decoder refuses because no file of it could be measured. A frame that cannot be read back is not written."),
      "MAGY" => throw new NotSupportedException(
        $"Video stream {stream.Index} asks for MAGY, the single code MagicYUV used before it gave each pixel format one of its own. Which layout such a frame would hold is not in the code, so it is refused rather than guessed at."),
      "M0RG" or "M0RA" or "M0Y0" or "M0Y2" or "M0Y4" or "M0G0"
        or "M2RG" or "M2RA" or "M4RG" or "M4RA" => throw new NotSupportedException(
        $"Video stream {stream.Index} asks for {name}, one of MagicYUV's codes for samples deeper than eight bits. How those samples are packed is published nowhere, so it is refused by name rather than written as though its samples were bytes."),
      _ => stream.BitsPerPixel switch {
        8 => ("M8G0", (byte)0x6B, PixelFormat.Gray8),
        32 => ("M8RA", (byte)0x66, PixelFormat.Rgba32),
        _ => ("M8RG", (byte)0x65, PixelFormat.Rgb24),
      },
    };

    return new(stream, CodecTag.FromCharacters(tag), formatByte, pixelFormat, predictor, slices);
  }

  /// <summary>Encodes one picture as one frame, which for this codec is always a key frame.</summary>
  public bool TryEncode(RawImage frame, long? presentationTimestamp, out CodedPacket packet) {
    ArgumentNullException.ThrowIfNull(frame);
    if (frame.Width != this._stream.Width || frame.Height != this._stream.Height)
      throw new InvalidDataException(
        $"The encoder was created for {this._stream.Width}x{this._stream.Height} pictures and was handed one of {frame.Width}x{frame.Height}.");

    if (!frame.HasEnoughPixelData)
      throw new InvalidDataException(
        $"A {frame.Width}x{frame.Height} {frame.Format} picture needs at least {frame.MinimumPixelDataLength} bytes and carries {frame.PixelData.Length}.");

    var converted = frame.Format == this._pixelFormat ? frame : FastRawImageConverter.Convert(frame, this._pixelFormat);
    if (!converted.HasEnoughPixelData)
      throw new InvalidDataException(
        $"Conversion to {this._pixelFormat} produced {converted.PixelData.Length} bytes where a {frame.Width}x{frame.Height} picture needs {converted.MinimumPixelDataLength}.");

    var data = this._Encode(this._Planes(converted));
    packet = new(
      StreamIndex: this._stream.Index,
      Data: data,
      PresentationTimestamp: presentationTimestamp,
      DecodeTimestamp: presentationTimestamp,
      IsKeyFrame: true);
    return true;
  }

  /// <summary>The stream this writes, which the package's own decoder accepts as it stands.</summary>
  public MediaStreamInfo DescribeStream() => this._stream;

  // ============================================================================================
  // The planes the codec codes
  // ============================================================================================

  /// <summary>
  /// Splits the picture into the planes the frame carries, in the order it carries them.
  /// </summary>
  /// <remarks>
  /// The colour layouts carry blue, green, red — green plainly and the other two as their distance
  /// from it, wrapping — with alpha after them, plain. The luminance layouts carry the picture's
  /// planes as they are, which for a subsampled picture are already the size the codec wants
  /// because both round the odd row and column up.
  /// </remarks>
  private byte[][] _Planes(RawImage picture) {
    var format = this._format;
    var width = picture.Width;
    var height = picture.Height;
    var count = width * height;
    var pixels = picture.PixelData;

    switch (format.ColourSpace) {
      case MagicYuvColourSpace.Grey:
        return [pixels.AsSpan(0, count).ToArray()];

      case MagicYuvColourSpace.Rgb: {
        var channels = format.HasAlpha ? 4 : 3;
        var blue = new byte[count];
        var green = new byte[count];
        var red = new byte[count];
        var alpha = format.HasAlpha ? new byte[count] : null;
        for (var i = 0; i < count; ++i) {
          var at = i * channels;
          var g = pixels[at + 1];
          green[i] = g;
          blue[i] = (byte)(pixels[at + 2] - g);
          red[i] = (byte)(pixels[at] - g);
          if (alpha != null)
            alpha[i] = pixels[at + 3];
        }

        return alpha != null ? [blue, green, red, alpha] : [blue, green, red];
      }

      default: {
        var planes = new byte[format.PlaneCount][];
        for (var plane = 0; plane < format.PlaneCount; ++plane) {
          var (planeWidth, planeHeight) = format.PlaneSize(plane, width, height);
          var (pictureWidth, pictureHeight) = picture.GetPlaneDimensions(plane);
          if (planeWidth != pictureWidth || planeHeight != pictureHeight)
            throw new InvalidDataException(
              $"Plane {plane} of a {width}x{height} {picture.Format} picture is {pictureWidth}x{pictureHeight} where the codec's is {planeWidth}x{planeHeight}.");

          planes[plane] = picture.GetPlaneData(plane).ToArray();
        }

        return planes;
      }
    }
  }

  // ============================================================================================
  // The frame
  // ============================================================================================

  private byte[] _Encode(byte[][] planes) {
    var format = this._format;
    var width = this._stream.Width;
    var height = this._stream.Height;
    var planeCount = format.PlaneCount;
    var slices = this._sliceCount;
    var pieceCount = planeCount * slices;

    // predict every piece and count what the prediction leaves, one count a plane
    var residuals = new byte[pieceCount][];
    var counts = new long[planeCount][];
    for (var plane = 0; plane < planeCount; ++plane) {
      counts[plane] = new long[256];
      var (planeWidth, planeHeight) = format.PlaneSize(plane, width, height);
      var sliceHeight = format.SliceHeight(plane, this._sliceHeight);
      for (var slice = 0; slice < slices; ++slice) {
        var firstRow = Math.Min(slice * sliceHeight, planeHeight);
        var lastRow = Math.Min(firstRow + sliceHeight, planeHeight);
        var residual = _Predict(planes[plane], planeWidth, firstRow, lastRow, this._predictor);
        residuals[slice * planeCount + plane] = residual;
        var planeCounts = counts[plane];
        foreach (var value in residual)
          ++planeCounts[value];
      }
    }

    var lengths = new byte[planeCount][];
    var codes = new uint[planeCount][];
    for (var plane = 0; plane < planeCount; ++plane) {
      lengths[plane] = MagicYuvCodeLengths.Choose(counts[plane], _LONGEST_CODE);
      codes[plane] = MagicYuvCodeLengths.Codes(lengths[plane]);
    }

    // size every piece, in the order they lie in the frame: every plane of slice nought, then of
    // slice one, and so on; each padded to four bytes as ffmpeg pads them
    var tablesEnd = MagicYuvFormat.HEADER_SIZE + 4 * (pieceCount + 1) + 1 + pieceCount + 256 * planeCount;
    var positions = new int[pieceCount];
    var sizes = new int[pieceCount];
    var raw = new bool[pieceCount];
    var total = tablesEnd;
    for (var slice = 0; slice < slices; ++slice)
      for (var plane = 0; plane < planeCount; ++plane) {
        var piece = slice * planeCount + plane;
        var residual = residuals[piece];
        var planeLengths = lengths[plane];
        var bits = 0L;
        foreach (var value in residual)
          bits += planeLengths[value];

        var coded = (int)((bits + 7) >> 3);
        raw[piece] = coded >= residual.Length;
        var size = 2 + (raw[piece] ? residual.Length : coded);
        sizes[piece] = (size + 3) & ~3;
        positions[piece] = total;
        total += sizes[piece];
      }

    var frame = new byte[total];
    var at = 0;
    MagicYuvFormat.Signature.CopyTo(frame, 0);
    at += 4;
    _WriteUInt32(frame, ref at, MagicYuvFormat.HEADER_SIZE);
    frame[at++] = MagicYuvFormat.VERSION_BYTE;
    frame[at++] = this._formatByte;
    frame[at++] = _LONGEST_CODE;
    frame[at++] = 0;
    frame[at++] = 0;
    frame[at++] = 0;
    frame[at++] = _CODER_TYPE;
    frame[at++] = 0;
    _WriteUInt32(frame, ref at, (uint)width);
    _WriteUInt32(frame, ref at, (uint)height);
    _WriteUInt32(frame, ref at, (uint)width);
    _WriteUInt32(frame, ref at, (uint)this._sliceHeight);

    // the offsets are relative to the end of the header, written plane first, and the map after
    // them names the piece each one belongs to
    _WriteUInt32(frame, ref at, (uint)(tablesEnd - MagicYuvFormat.HEADER_SIZE));
    for (var plane = 0; plane < planeCount; ++plane)
      for (var slice = 0; slice < slices; ++slice)
        _WriteUInt32(frame, ref at, (uint)(positions[slice * planeCount + plane] - MagicYuvFormat.HEADER_SIZE));

    frame[at++] = (byte)planeCount;
    for (var plane = 0; plane < planeCount; ++plane)
      for (var slice = 0; slice < slices; ++slice)
        frame[at++] = (byte)(slice * planeCount + plane);

    for (var plane = 0; plane < planeCount; ++plane) {
      lengths[plane].CopyTo(frame, at);
      at += 256;
    }

    if (at != tablesEnd)
      throw new InvalidOperationException($"The tables end at byte {at} where the offsets say {tablesEnd}.");

    for (var piece = 0; piece < pieceCount; ++piece) {
      var start = positions[piece];
      var residual = residuals[piece];
      frame[start] = raw[piece] ? _UNCOMPRESSED : _CODED;
      frame[start + 1] = (byte)this._predictor;
      if (raw[piece]) {
        residual.CopyTo(frame, start + 2);
        continue;
      }

      var planeCodes = codes[piece % planeCount];
      var planeLengths = lengths[piece % planeCount];
      var bits = new _BitWriter(frame, start + 2);
      foreach (var value in residual)
        bits.Write(planeCodes[value], planeLengths[value]);

      bits.Flush();
    }

    return frame;
  }

  /// <summary>
  /// The differences a slice codes: every row starts again from the sample above it, the first row
  /// of the slice from nought and then from the left, whichever predictor the slice names.
  /// </summary>
  private static byte[] _Predict(byte[] plane, int width, int firstRow, int lastRow, MagicYuvPredictor predictor) {
    var residual = new byte[(lastRow - firstRow) * width];
    var to = 0;
    for (var y = firstRow; y < lastRow; ++y) {
      var row = y * width;
      for (var x = 0; x < width; ++x) {
        var at = row + x;
        byte predicted;

        if (x == 0)
          predicted = y == firstRow ? (byte)0 : plane[at - width];
        else if (y == firstRow)
          predicted = plane[at - 1];
        else {
          var left = plane[at - 1];
          var above = plane[at - width];
          var aboveLeft = plane[at - width - 1];
          var gradient = (byte)(left + above - aboveLeft);
          predicted = predictor switch {
            MagicYuvPredictor.Left => left,
            MagicYuvPredictor.Gradient => gradient,
            _ => _Median(left, above, gradient),
          };
        }

        residual[to++] = (byte)(plane[at] - predicted);
      }
    }

    return residual;
  }

  private static byte _Median(byte a, byte b, byte c) {
    if (a > b)
      (a, b) = (b, a);

    return c < a ? a : c > b ? b : c;
  }

  private static void _WriteUInt32(byte[] target, ref int at, uint value) {
    target[at] = (byte)value;
    target[at + 1] = (byte)(value >> 8);
    target[at + 2] = (byte)(value >> 16);
    target[at + 3] = (byte)(value >> 24);
    at += 4;
  }

  /// <summary>Writes a slice's bits: most significant first, straight into the bytes.</summary>
  private struct _BitWriter(byte[] target, int at) {

    private ulong _held;
    private int _heldBits;

    internal void Write(uint code, int length) {
      this._held = (this._held << length) | code;
      this._heldBits += length;
      while (this._heldBits >= 8) {
        this._heldBits -= 8;
        target[at++] = (byte)(this._held >> this._heldBits);
      }
    }

    internal void Flush() {
      if (this._heldBits == 0)
        return;

      target[at++] = (byte)(this._held << (8 - this._heldBits));
      this._heldBits = 0;
    }
  }
}
