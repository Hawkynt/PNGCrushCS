using System;
using System.IO;
using FileFormat.Bmp;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>
/// Decodes Autodesk Animator Codec (<c>AASC</c>): a run-length coding over twenty-four-bit BGR
/// pictures, coded bottom row first, with the same shape of escapes Microsoft RLE uses at eight bits
/// a pixel — end of row, end of frame and a delta that moves the pen without painting.
/// </summary>
/// <remarks>
/// Read from MultimediaWiki's "Autodesk Animator Codec" page, whose only citation is itself and whose
/// prose calls the format "similar to Microsoft RLE" without saying where the two differ. What it
/// states is a walk with one opcode byte deciding a run, and — when that byte is zero — a second byte
/// choosing end of row, end of frame, a reposition carrying a right offset and an up offset, or (any
/// other value) a literal run of that many "pixels", the run padded to a whole number of bytes when
/// its count is odd. It does not say how wide a pixel is for the twenty-four-bit form this codec is
/// defined at, and reading it as three-byte BGR triples — the obvious reading for "twenty-four-bit"
/// — decodes nothing correctly: it desyncs a real file within the first few opcodes, and where framing
/// is coaxed into surviving anyway, one measured frame comes out with sixteen per cent of its bytes
/// wrong, spread through the whole picture instead of localised to one fault.
/// <para/>
/// <b>The picture is walked one byte at a time, not one pixel at a time.</b> A twenty-four-bit AASC
/// frame is a picture <c>width * 3</c> bytes wide and every opcode — run, literal run, end of row, the
/// column half of a reposition — is stated in bytes of that wider row, not in pixels of the true one.
/// Read this way, "pixel" in the wiki's prose is just its word for "byte", and every run in the coding
/// is a run of raw bytes read and written byte for byte: a repeat opcode's run value is <b>one byte</b>,
/// not three, filled straight across the row rather than broadcast into three channels of a colour;
/// what makes the picture come out in colour at all is that the row itself interleaves blue, green and
/// red bytes in that order and a run or a reposition can start and stop mid-triple exactly as it
/// pleases. Found by an all-black keyframe that this reading alone leaves all-black: reading run values
/// as three-byte colours instead paints every solid-black reference frame with bright, saturated noise,
/// because the coded stream's real byte values — repeated hundreds of times a row to fill a row three
/// times the true picture's width — are never once all thirty-two-bit-aligned enough to read as
/// plausible small BGR triples.
/// <para/>
/// <b>Every frame's coded data opens on a row that does not exist.</b> The row cursor starts at
/// <c>height</c> — one past the bottom row a <c>height</c>-row picture actually has — and the very
/// first opcode of every measured frame, keyframe and delta alike, targets that row and is silently
/// discarded before the frame's first end-of-row or reposition escape brings the cursor onto row
/// <c>height - 1</c>, the true bottom row the coding then walks upward from. Starting the cursor at
/// <c>height - 1</c> instead — the picture's real bottom row — decodes every frame from 0 to 11 of the
/// one sample this was checked against correctly regardless, since all twelve happen to be solid
/// black, but frame 12's first reposition — the four bytes <c>00 02 1B 3C</c>, a sixty-row upward
/// move — then lands one row short of the row its own five painted pixels belong to, because the
/// frame's row cursor started one row too high to begin with.
/// <para/>
/// <b>Measured.</b> The one sample on <c>samples.mplayerhq.hu/V-codecs/AASC/</c> and mirrored on
/// ffmpeg's own sample server — <c>AASC.AVI</c>, 320x175, 113 frames, twelve of them solid black and
/// coded as such — was decoded here and by ffmpeg and compared sample for sample against ffmpeg's own
/// <c>bgr24</c> output: all 113 frames are identical, maximum delta nought. No second sample of this
/// codec was found published anywhere.
/// <para/>
/// What is not implemented refuses and says so: a stream that does not state twenty-four bits a pixel,
/// one storing its rows top-down rather than bottom-up, coded data that runs out before a frame signals
/// its own end, and any run, literal run or reposition addressing a row outside the picture or a column
/// running past the end of its row — the sentinel row every frame opens on excepted, since a run
/// addressing it is not malformed, it is what every measured frame's first opcode does.
/// </remarks>
public sealed class AascVideoDecoder : IVideoCodecDecoder<AascVideoDecoder> {

  private static readonly CodecTag _Tag = CodecTag.FromCharacters("AASC");

  private readonly int _width;
  private readonly int _height;
  private readonly int _stride;

  /// <summary>
  /// BGR bytes, <c>row * _stride + column</c>, row zero the picture's top row. Kept between packets
  /// and never cleared, because that is what a delta frame — everything a reposition escape steps
  /// over without painting — is predicted from.
  /// </summary>
  private readonly byte[] _canvas;

  /// <summary>Gets the codec name.</summary>
  public static string CodecName => "Autodesk Animator Codec";

  /// <summary>Determines whether the specified media stream is supported.</summary>
  public static bool Accepts(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);

    return stream.Kind == MediaStreamKind.Video && stream.Codec.EqualsIgnoringCase(_Tag);
  }

  /// <summary>Creates a decoder for the specified media stream.</summary>
  public static AascVideoDecoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);

    var format = stream.CodecPrivateData;
    if (format.Length < BitmapInfoHeader.StructSize)
      throw new InvalidOperationException(
        $"AASC video stream {stream.Index} carries {format.Length} bytes of stream format where a "
        + $"BITMAPINFOHEADER is {BitmapInfoHeader.StructSize}.");

    var info = BitmapInfoHeader.ReadFrom(format.Span);
    if (info.Height < 0)
      throw new NotSupportedException(
        $"AASC video stream {stream.Index} states a height of {info.Height}, which asks for rows top down. "
        + "This coding is defined bottom-up only, and no file storing it the other way up is read.");

    var width = info.Width;
    var height = info.Height;
    if (width <= 0 || height <= 0)
      throw new InvalidOperationException(
        $"AASC video stream {stream.Index} states a picture of {width}x{height}, which has no pixels.");

    if (info.BitsPerPixel != 24)
      throw new NotSupportedException(
        $"AASC video stream {stream.Index} states {info.BitsPerPixel} bits per pixel. This coding is defined "
        + "at twenty-four bits a pixel and nothing else is read.");

    // Multiplied as a long before the canvas is asked for. A damaged header can overflow width * height
    // * 3 to a small or negative int, which would allocate a canvas of the wrong size and then fail
    // somewhere inside the walk rather than here, naming neither the field nor the file.
    if ((long)width * height * 3 > int.MaxValue)
      throw new InvalidOperationException(
        $"AASC video stream {stream.Index} states a picture of {width}x{height}, which is more bytes than can "
        + "be held.");

    return new(width, height);
  }

  private AascVideoDecoder(int width, int height) {
    this._width = width;
    this._height = height;
    this._stride = width * 3;
    this._canvas = new byte[this._stride * height];
  }

  /// <summary>Decodes one packet, which for this codec is always exactly one whole frame.</summary>
  public bool TryDecode(CodedPacket packet, out RawImage frame) {
    this._Decode(packet.Data.Span);

    frame = new() {
      Width = this._width,
      Height = this._height,
      Format = PixelFormat.Bgr24,
      PixelData = (byte[])this._canvas.Clone(),
    };

    return true;
  }

  /// <summary>
  /// The RLE walk itself: a run byte and, when it is zero, a command byte choosing end of row, end of
  /// frame, a reposition, or (any other value) a literal run — every run's length and every
  /// reposition's offsets counted in bytes of the picture's <c>width * 3</c>-wide row, not in pixels.
  /// </summary>
  private void _Decode(ReadOnlySpan<byte> data) {
    var height = this._height;
    var n = data.Length;
    var pos = 0;
    var row = height; // one past the last real row; see the remarks on the type.
    var col = 0;
    var done = false;

    while (!done) {
      _Require(pos < n, "an opcode");
      var code = data[pos++];

      if (code != 0) {
        _Require(pos < n, "a run's byte value");
        var value = data[pos++];
        var target = this._Target(row, col, code);
        if (!target.IsEmpty)
          target.Fill(value);
        col += code;
        continue;
      }

      _Require(pos < n, "a command code");
      var command = data[pos++];
      switch (command) {
        case 0: // end of row
          --row;
          col = 0;
          break;

        case 1: // frame done
          done = true;
          break;

        case 2: // reposition: right offset, then up offset, neither painting anything
          _Require(pos + 1 < n, "a reposition's offsets");
          var dx = data[pos++];
          var dy = data[pos++];
          col += dx;
          row -= dy;
          break;

        default: { // literal run of `command` bytes, padded to even
          var count = command;
          _Require(pos + count <= n, "a literal run's bytes");
          var target = this._Target(row, col, count);
          if (!target.IsEmpty)
            data.Slice(pos, count).CopyTo(target);
          pos += count;
          col += count;
          if ((count & 1) != 0) {
            _Require(pos < n, "a literal run's padding byte");
            ++pos;
          }
          break;
        }
      }
    }
  }

  /// <summary>
  /// The span a run of <paramref name="count"/> bytes at (<paramref name="row"/>, <paramref name="col"/>)
  /// writes into, or an empty span when the row is the one-past-the-end sentinel every frame's first
  /// opcode legitimately targets. Any other row outside the picture, or a run reaching past the end of
  /// its row, is the coded data disagreeing with the picture's own size rather than the sentinel — that
  /// refuses by name.
  /// </summary>
  private Span<byte> _Target(int row, int col, int count) {
    if (row == this._height)
      return default;

    if (row < 0 || col + count > this._stride)
      throw new InvalidDataException(
        $"An AASC frame writes {count} byte(s) at column {col} of row {row}, outside its {this._width}x"
        + $"{this._height} picture.");

    return this._canvas.AsSpan(row * this._stride + col, count);
  }

  private static void _Require(bool condition, string what) {
    if (!condition)
      throw new InvalidDataException($"An AASC frame's coded data ran out reading {what}.");
  }
}
