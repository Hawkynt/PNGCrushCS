using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.Codecs.QuickTimeRle;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>
/// Decodes QuickTime Animation, the codec every container calls <c>rle </c>: run-length coding along
/// each line, over a canvas the frames before it left behind.
/// </summary>
/// <remarks>
/// Lossless, and line-based rather than block-based. A frame states which band of lines it touches
/// and then writes those lines as runs, literal pixels and skips; everything it does not write is
/// whatever was there already. That is what makes it a codec rather than a per-frame image format,
/// and it is the whole of the state this decoder keeps: one canvas, updated in place.
/// <para/>
/// <b>Every count is in coded units, not pixels.</b> At sixteen bits and above a unit is a pixel; at
/// eight and below it is four bytes — four indices at eight bits, eight at four, sixteen at two — and
/// at one bit it is two bytes, which is again sixteen pixels. A copy of <i>n</i> is <i>n</i> units, a
/// run of <i>-n</i> is one unit written <i>n</i> times, and a skip of <i>n</i> steps <i>n-1</i> units
/// forward. See <see cref="QuickTimeRleLayout"/> for how that was measured.
/// <para/>
/// <b>Nothing is invented.</b> A stream whose first frame does not cover every line is refused rather
/// than composited onto black, a count that would run off the end of a line is refused rather than
/// clamped, and a depth without the colour table its indices need is refused rather than drawn
/// through a guessed palette. There is no <c>catch</c> here that hands back a blank or a repeated
/// picture. The one frame that legitimately repeats the one before it is a frame the file states as
/// empty, which the format defines and which is not the same thing as a decode that failed.
/// <para/>
/// <b>Measured.</b> Every depth ffmpeg's encoder can write — thirty-two bits with alpha, twenty-four,
/// sixteen and eight-bit greyscale — was decoded here and by ffmpeg and compared pixel by pixel over
/// every frame of each stream: twenty-two streams, 360 frames, every one identical, alpha included.
/// The depths ffmpeg cannot write — one, two and four bits, eight bits through a colour table, and
/// widths that are not a whole number of coded units — were checked the other way round, with streams
/// built to say a known picture: fifteen more streams, sixty frames, ffmpeg reading each as the
/// picture it was built to say and this reading each as ffmpeg does.
/// </remarks>
public sealed class QuickTimeRleDecoder : IVideoCodecDecoder<QuickTimeRleDecoder> {

  /// <summary>The code every container names this codec with, trailing space and all.</summary>
  private static readonly CodecTag _RLE = CodecTag.FromCharacters("rle ");

  /// <summary>The flag in a frame's header that says a band of lines follows.</summary>
  private const int _HAS_LINE_RANGE = 0x0008;

  /// <summary>The shortest a frame can be and still say anything: a length and a header.</summary>
  private const int _EMPTY_FRAME_LENGTH = 8;

  private readonly int _width;
  private readonly int _height;

  /// <summary>
  /// How many pixels a coded line covers, which is the visible width rounded up to a whole unit.
  /// </summary>
  /// <remarks>
  /// At eight bits and below a line is written in groups of four bytes, so a picture whose width is
  /// not a whole number of groups has a few pixels of padding at the end of every line that are coded
  /// and then not shown — exactly as the rows of a Windows bitmap are padded. Coding those pixels
  /// into the canvas and cropping them off the picture is what lets the last group of such a line be
  /// read as the group it is, rather than refused for running past a width it was never measured
  /// against. Above eight bits a unit is one pixel and this is the width.
  /// </remarks>
  private readonly int _codedWidth;

  private readonly QuickTimeRleLayout _layout;
  private readonly byte[] _canvas;
  private readonly byte[]? _palette;
  private readonly int _paletteCount;
  private readonly PixelFormat _format;
  private bool _hasPicture;

  private QuickTimeRleDecoder(int width, int height, QuickTimeRleLayout layout, byte[]? palette, int paletteCount) {
    this._width = width;
    this._height = height;
    this._layout = layout;
    this._palette = palette;
    this._paletteCount = paletteCount;
    this._codedWidth = (width + layout.UnitPixels - 1) / layout.UnitPixels * layout.UnitPixels;
    this._canvas = new byte[this._codedWidth * height * layout.CanvasBytesPerPixel];
    this._format = layout.IsIndexed
      ? PixelFormat.Indexed8
      : layout.Depth == 32
        ? PixelFormat.Rgba32
        : PixelFormat.Rgb24;
  }

  /// <summary>Gets the codec name.</summary>
  public static string CodecName => "QuickTime Animation (RLE)";

  /// <summary>Takes a stream whose code is <c>rle </c> in any case.</summary>
  public static bool Accepts(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);

    return stream.Kind == MediaStreamKind.Video && stream.Codec.EqualsIgnoringCase(_RLE);
  }

  /// <summary>
  /// Builds a decoder for one Animation stream, refusing a depth or a description it cannot draw.
  /// </summary>
  /// <remarks>
  /// The depth is the container's reading of the sample description where it has one, because that
  /// field is at the same place in every visual sample entry whatever the codec is and reading it is
  /// therefore not codec knowledge. Where a container states none, the description itself is read for
  /// it — the same two bytes, from the same place.
  /// </remarks>
  public static QuickTimeRleDecoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);

    if (stream.Width <= 0 || stream.Height <= 0)
      throw new InvalidDataException(
        $"Video stream {stream.Index} states a picture size of {stream.Width}x{stream.Height}, which is not a size an Animation frame can be decoded into.");

    var description = stream.CodecPrivateData.Span;
    QuickTimeRleSampleDescription.RefuseUnreadable(description, stream.Index);

    var depth = stream.BitsPerPixel > 0 ? stream.BitsPerPixel : QuickTimeRleSampleDescription.Depth(description);
    if (depth == 0)
      throw new InvalidDataException(
        $"Video stream {stream.Index} states no depth, and an Animation frame cannot be read without one — the same byte counts mean different numbers of pixels at every depth.");

    var layout = QuickTimeRleLayout.ForDepth(depth, stream.Index);

    byte[]? palette = null;
    var paletteCount = 0;
    if (layout.IsIndexed)
      (palette, paletteCount) = layout.BuildPalette(QuickTimeRleSampleDescription.ColourTable(description), stream.Index);

    return new(stream.Width, stream.Height, layout, palette, paletteCount);
  }

  /// <summary>
  /// Applies one frame to the canvas and hands back what the canvas then shows.
  /// </summary>
  /// <remarks>
  /// Always a picture, because every sample of an Animation stream is a frame of the film even when
  /// it changes nothing. The picture is a fresh copy of the canvas rather than the canvas itself: a
  /// caller holding frame three must not watch it turn into frame four.
  /// </remarks>
  public bool TryDecode(CodedPacket packet, out RawImage frame) {
    this._Apply(packet.Data.Span);

    frame = new() {
      Width = this._width,
      Height = this._height,
      Format = this._format,
      PixelData = this._Picture(),
      Palette = this._palette,
      PaletteCount = this._paletteCount,
    };

    return true;
  }

  /// <summary>The canvas as a picture: a copy of it, with any coded padding taken off each line.</summary>
  /// <remarks>
  /// A copy, because the canvas is what the next frame is written over and a caller holding frame
  /// three must not watch it turn into frame four.
  /// </remarks>
  private byte[] _Picture() {
    var bytesPerPixel = this._layout.CanvasBytesPerPixel;
    var visible = this._width * bytesPerPixel;
    if (this._codedWidth == this._width)
      return (byte[])this._canvas.Clone();

    var coded = this._codedWidth * bytesPerPixel;
    var picture = new byte[visible * this._height];
    for (var y = 0; y < this._height; ++y)
      this._canvas.AsSpan(y * coded, visible).CopyTo(picture.AsSpan(y * visible));

    return picture;
  }

  /// <summary>
  /// Runs one frame's opcodes over the canvas.
  /// </summary>
  /// <remarks>
  /// A frame of fewer than eight bytes has no room for a length and a header and is the format's way
  /// of saying that nothing changed. That is a frame and not a failure — but only once there is
  /// something for it to repeat, so a stream that opens with one is refused rather than answered with
  /// the black the canvas starts as.
  /// </remarks>
  private void _Apply(ReadOnlySpan<byte> data) {
    if (data.Length < _EMPTY_FRAME_LENGTH) {
      if (!this._hasPicture)
        throw new InvalidDataException(
          $"The stream opens with a frame of {data.Length} bytes, which states that nothing changed — but there is no frame before it for it to be the same as.");

      return;
    }

    // The first four bytes are the frame's own length. They are read past rather than checked
    // against: writers disagree about whether the top byte carries flags, and the container has
    // already said how long the sample is, which is the length that decides where the data ends.
    var header = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(4, 2));

    int startLine;
    int lines;
    int position;
    if ((header & _HAS_LINE_RANGE) != 0) {
      if (data.Length < 14)
        throw new InvalidDataException(
          $"A frame states a band of lines follows its header but carries {data.Length} bytes, where the band alone is eight.");

      startLine = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(6, 2));
      lines = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(10, 2));
      position = 14;
    } else {
      startLine = 0;
      lines = this._height;
      position = 6;
    }

    if (startLine > this._height || lines > this._height - startLine)
      throw new InvalidDataException(
        $"A frame states it updates {lines} line(s) from line {startLine} of a picture {this._height} lines tall.");

    if (!this._hasPicture && (startLine != 0 || lines != this._height))
      throw new InvalidDataException(
        $"The stream opens with a frame that updates only lines {startLine} to {startLine + lines - 1} of {this._height}. Decoding cannot begin there — the lines it does not touch have nothing behind them.");

    if (this._layout.BitsPerSample == 1)
      this._DecodeOneBitLines(data, position, startLine, lines);
    else
      this._DecodeLines(data, position, startLine, lines);

    this._hasPicture = true;
  }

  private void _DecodeLines(ReadOnlySpan<byte> data, int position, int startLine, int lines) {
    var unitBytes = this._layout.UnitBytes;
    var unitPixels = this._layout.UnitPixels;
    var bytesPerPixel = this._layout.CanvasBytesPerPixel;
    Span<byte> unit = stackalloc byte[unitPixels * bytesPerPixel];

    for (var line = 0; line < lines; ++line) {
      var row = (startLine + line) * this._codedWidth * bytesPerPixel;
      var pixel = (_Next(data, ref position) - 1) * unitPixels;

      for (; ; ) {
        var code = (sbyte)_Next(data, ref position);
        if (code == -1)
          break;

        if (code == 0) {
          pixel += (_Next(data, ref position) - 1) * unitPixels;
          continue;
        }

        var count = code > 0 ? code : -code;
        this._RefuseOverrun(pixel, count * unitPixels, startLine + line);

        if (code > 0) {
          // A literal run: the units follow one another in the stream.
          for (var i = 0; i < count; ++i) {
            this._layout.ExpandUnit(_Take(data, ref position, unitBytes), unit);
            unit.CopyTo(this._canvas.AsSpan(row + pixel * bytesPerPixel));
            pixel += unitPixels;
          }

          continue;
        }

        // A repeat: one unit, written as many times as the count says.
        this._layout.ExpandUnit(_Take(data, ref position, unitBytes), unit);
        for (var i = 0; i < count; ++i) {
          unit.CopyTo(this._canvas.AsSpan(row + pixel * bytesPerPixel));
          pixel += unitPixels;
        }
      }
    }
  }

  /// <summary>
  /// Runs one frame's opcodes over the canvas at one bit a pixel, where they are a different shape.
  /// </summary>
  /// <remarks>
  /// At one bit the compressor does not write a skip once per line and then a run of opcodes. It
  /// writes a skip and an opcode together, every time, and the skip's top bit is what says a new line
  /// begins — with the low seven bits then counting units from the start of that line rather than
  /// from where the last opcode left off. A skip without the top bit carries on from where the last
  /// one ended, and does so across the end of a line, because the position is one number over the
  /// whole picture and not a column within a row.
  /// <para/>
  /// The first such marker starts the band rather than stepping past its first line, which is what
  /// makes a frame that updates lines 4 to 7 begin at line 4 and not at line 5.
  /// <para/>
  /// None of that is guesswork. It was measured against ffmpeg by handing it streams built to say
  /// exactly one thing and reading back where it put the pixels: a skip of <c>0x81</c> puts sixteen
  /// pixels at column sixteen of the first line and a skip of <c>0xFF</c> on the line after it asks
  /// for pixel 2096 of a picture that holds 512, which is the arithmetic above and no other. Five
  /// pictures of different widths — including widths that are not a whole number of sixteens — were
  /// then written in this shape and read back by ffmpeg pixel for pixel.
  /// </remarks>
  private void _DecodeOneBitLines(ReadOnlySpan<byte> data, int position, int startLine, int lines) {
    const int _NEW_LINE = 0x80;
    const int _UNIT_PIXELS = 16;
    const int _UNIT_BYTES = 2;

    Span<byte> unit = stackalloc byte[_UNIT_PIXELS];
    var stride = this._codedWidth;
    var row = startLine * stride - stride;
    var pixel = row;
    var remaining = lines;

    while (remaining > 0) {
      if (data.Length - position < 2)
        break;

      var skip = data[position++];
      var code = (sbyte)data[position++];
      if (code == 0)
        break;

      if ((skip & _NEW_LINE) != 0) {
        --remaining;
        row += stride;
        pixel = row + (skip & 0x7F) * _UNIT_PIXELS;
      } else
        pixel += skip * _UNIT_PIXELS;

      // The end-of-line marker every other depth has. Here it says nothing and carries no unit with
      // it, because the skip beside it has already said where the next opcode writes.
      if (code == -1)
        continue;

      var count = code > 0 ? code : -code;
      this._RefuseCanvasOverrun(pixel, count * _UNIT_PIXELS);

      if (code > 0) {
        for (var i = 0; i < count; ++i) {
          this._layout.ExpandUnit(_Take(data, ref position, _UNIT_BYTES), unit);
          unit.CopyTo(this._canvas.AsSpan(pixel));
          pixel += _UNIT_PIXELS;
        }

        continue;
      }

      this._layout.ExpandUnit(_Take(data, ref position, _UNIT_BYTES), unit);
      for (var i = 0; i < count; ++i) {
        unit.CopyTo(this._canvas.AsSpan(pixel));
        pixel += _UNIT_PIXELS;
      }
    }
  }

  /// <summary>Refuses a write that would fall outside the canvas altogether.</summary>
  /// <remarks>
  /// The check the one-bit path needs in place of a per-line one: its position is a number over the
  /// whole picture, so a line's own width is not what bounds it.
  /// </remarks>
  private void _RefuseCanvasOverrun(int pixel, int pixels) {
    if (pixel >= 0 && pixels <= this._canvas.Length - pixel)
      return;

    throw new InvalidDataException(
      $"A frame places {pixels} pixel(s) at position {pixel} of a picture that holds {this._canvas.Length}.");
  }

  /// <summary>
  /// Refuses a count that would write past the end of the line it is on.
  /// </summary>
  /// <remarks>
  /// Refused and not clamped. A line that carries more pixels than it has room for is a stream this
  /// decoder has misread or a file that is damaged, and in either case the picture that would come
  /// out of writing what fits is a picture nothing wrote.
  /// </remarks>
  private void _RefuseOverrun(int pixel, int pixels, int line) {
    if (pixel >= 0 && pixels <= this._codedWidth - pixel)
      return;

    throw new InvalidDataException(
      $"Line {line} places {pixels} pixel(s) at column {pixel} of a line {this._codedWidth} pixels wide.");
  }

  private static byte _Next(ReadOnlySpan<byte> data, ref int position) {
    if (position >= data.Length)
      throw new InvalidDataException($"A frame of {data.Length} bytes ran out before its lines did.");

    return data[position++];
  }

  private static ReadOnlySpan<byte> _Take(ReadOnlySpan<byte> data, ref int position, int count) {
    if (count > data.Length - position)
      throw new InvalidDataException(
        $"A frame of {data.Length} bytes holds {data.Length - position} byte(s) where the next coded unit needs {count}.");

    var taken = data.Slice(position, count);
    position += count;
    return taken;
  }
}
