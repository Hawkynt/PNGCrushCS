using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.Core;
using FileFormat.Iff;

namespace FileFormat.Codecs;

/// <summary>
/// Decodes IFF ANIM video (<c>ANIM</c>): the Amiga's own CEL animation format, whose first frame is an
/// ordinary IFF ILBM picture and whose later frames are either that picture again or the coded
/// difference from an earlier one — plane-major bit planar throughout, read either straight through a
/// palette or through Hold-And-Modify, exactly the two pixel encodings this library's CDXL decoder
/// already reads.
/// </summary>
/// <remarks>
/// Each packet is one whole <c>FORM ILBM</c> — <see cref="Formats.Anim.AnimContainer"/> hands it over
/// undecoded — and its BMHD, ANHD, CMAP and BODY or DLTA chunks are parsed with <c>FileFormat.Iff</c>'s
/// generic chunk reader rather than a second parser written for the same boxes: one frame's few hundred
/// bytes is exactly the shape that reader is for, where the whole file is not. What genuinely differs
/// from an ordinary still ILBM is everything this decoder actually adds — the animation header, the
/// double-buffering a delta frame is measured against, and the delta coding itself — none of which the
/// still reader has any notion of.
/// <para/>
/// <b>Only compression method 5, Byte Vertical Delta, is decoded.</b> The original ANIM specification —
/// "An IFF Format For CEL Animations" by Gary Bonham of Sparta Inc. and Aegis Development, mirrored in
/// full at <c>wiki.amigaos.net/wiki/ANIM_IFF_CEL_Animations</c> — names five compression methods and says
/// outright that "the only one currently being placed in new code is the vertical run length encoded
/// byte encoding developed by Jim Kent," which is method 5. Real files bear this out: every ANIM sample
/// measured that carries motion uses it. Methods 1 through 4, 6, 7 and 8 are refused by name as
/// unverified rather than guessed at from the same document's prose alone; method 74 ("J", Eric Graham's
/// compression) has no published description at all — the specification's own words are "details to be
/// released later," and they never were.
/// <para/>
/// <b>The buffer a delta modifies is not the picture just shown.</b> ANIM was designed for hardware
/// double-buffering: two picture buffers exist, the displayed one and a hidden one being built for next.
/// An <c>ANHD</c> chunk's <c>interleave</c> field states which: zero (its default) means "two frames
/// back" — the buffer that was hidden while the current picture was shown, which becomes the new hidden
/// buffer once the delta is applied to it — and this decoder keeps exactly two plane-major buffers and
/// alternates which one a delta lands on to reproduce that. Every real file measured states an
/// interleave of zero; the specification's own account of the DPaint "Anim Brush" variant, which uses one
/// (modifying the immediately previous frame) together with Exclusive-Or rather than direct storage, is
/// refused by name, since no measured file exercises it and the specification's own author for that
/// convention notes the bit he uses to signal Exclusive-Or is his own extension rather than a documented
/// one.
/// <para/>
/// <b>The wire format and the working format are not the same layout.</b> An ILBM's <c>BODY</c> is stored
/// interleaved — a scanline's bitplane rows one after another, exactly what <see
/// cref="Core.PlanarConverter.IlbmPlanarToChunky"/> reads — but delta coding was designed for the Amiga's
/// own in-memory bitmap, whose planes are separate, contiguous arrays, "plane-major" here. Applying the
/// delta ops to an interleaved buffer using the specification's own column and row-stride arithmetic
/// reproduces roughly two thirds of a frame's pixels correctly and nothing resembling the rest, which is
/// how this was caught: a picture decoded that way is no closer to right than one decoded with the wrong
/// palette. The buffer this decoder mutates is transposed to plane-major immediately after a keyframe's
/// interleaved <c>BODY</c> is unpacked, and read back through the same plane-major arithmetic <see
/// cref="Codecs.CdxlVideoDecoder"/> already uses for CDXL's own bit-planar pictures.
/// <para/>
/// <b>Hold-And-Modify's four- and six-bit modify values both widen to eight bits by repeating their own
/// low bits</b> — <c>(value &lt;&lt; shift) | (value &gt;&gt; (bits - shift))</c> — which is not what
/// CDXL's own HAM8 needs: there, the red channel disagrees with the oracle under either widening rule and
/// is refused outright. Here the same rule that works for HAM6 also reaches exact equality at HAM8 on
/// every channel, which says CDXL's HAM8 discrepancy is particular to that format or that oracle rather
/// than a fact about Hold-And-Modify in general.
/// <para/>
/// <b>Measured.</b> Four files from <c>samples.ffmpeg.org/anim/</c> — <c>anim5_1bpp.anim</c> (one
/// bitplane, direct palette), <c>anim5_8bpp.anim</c> (eight bitplanes, direct palette),
/// <c>anim5_ham6.anim</c> and <c>anim5_ham8.anim</c> (six and eight bitplanes, Hold-And-Modify), all
/// 160x120 and 123 frames each — were decoded here and by ffmpeg and compared sample for sample against
/// ffmpeg's own <c>rgb24</c> output: all 492 frames are identical, maximum delta nought.
/// <para/>
/// <b>What is not implemented refuses and says so:</b> any compression method other than 0 (a whole
/// picture, spelled out again rather than assumed unchanged) and 5; an interleave other than zero or one;
/// any of Anim5's option bits set, since no measured file sets any and the one documented use of them is
/// its own author's stated extension rather than the specification's; more than eight bitplanes, the
/// limit the delta chunk's own pointer table states; a delta chunk whose plane pointers run past its own
/// length; and a delta or a picture arriving before any keyframe has established a picture size.
/// </remarks>
public sealed class AnimVideoDecoder : IVideoCodecDecoder<AnimVideoDecoder> {

  private const int _OP_DIRECT = 0;
  private const int _OP_BYTE_VERTICAL_DELTA = 5;
  private const int _HAM_FLAG = 0x0800;

  private static readonly CodecTag _Tag = CodecTag.FromCharacters("ANIM");

  private int _width, _height, _planes, _bytesPerRow, _planeSize;
  private byte[]?[] _buffers = new byte[2][];
  private int _current;
  private byte[] _palette = [];
  private bool _isHam;
  private bool _hasKeyframe;

  public static string CodecName => "IFF ANIM Video";

  public static bool Accepts(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);

    return stream.Kind == MediaStreamKind.Video && stream.Codec.EqualsIgnoringCase(_Tag);
  }

  public static AnimVideoDecoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);

    return new();
  }

  private AnimVideoDecoder() { }

  public bool TryDecode(CodedPacket packet, out RawImage frame) {
    var span = packet.Data.Span;
    if (span.Length < 12 || span[8] != (byte)'I' || span[9] != (byte)'L' || span[10] != (byte)'B' || span[11] != (byte)'M')
      throw new InvalidDataException("An IFF ANIM video packet does not open with 'FORM', a size, and 'ILBM'.");

    var iff = IffReader.FromSpan(span);

    byte[]? bmhd = null, anhd = null, cmap = null, camg = null, body = null, dlta = null;
    foreach (var chunk in iff.Chunks)
      switch (chunk.ChunkId.ToString()) {
        case "BMHD": bmhd = chunk.Data; break;
        case "ANHD": anhd = chunk.Data; break;
        case "CMAP": cmap = chunk.Data; break;
        case "CAMG": camg = chunk.Data; break;
        case "BODY": body = chunk.Data; break;
        case "DLTA": dlta = chunk.Data; break;
      }

    if (bmhd != null)
      this._ReadBitmapHeader(bmhd);
    if (camg is { Length: >= 4 })
      this._isHam = (BinaryPrimitives.ReadUInt32BigEndian(camg) & _HAM_FLAG) != 0;
    if (cmap != null)
      this._palette = cmap;

    if (body != null) {
      if (this._width <= 0 || this._height <= 0)
        throw new InvalidDataException("An IFF ANIM keyframe states no picture size (no BMHD).");

      var compression = bmhd != null && bmhd.Length >= 11 ? bmhd[10] : 0;
      var interleaved = _UnpackBody(body, compression, this._planes * this._planeSize);
      var planeMajor = _InterleavedToPlaneMajor(interleaved, this._planes, this._bytesPerRow, this._height);
      this._buffers[0] = planeMajor;
      this._buffers[1] = (byte[])planeMajor.Clone();
      this._current = 0;
    } else if (dlta != null) {
      if (!this._hasKeyframe)
        throw new InvalidDataException("An IFF ANIM delta frame arrived before any keyframe established a picture size.");

      if (anhd is not { Length: >= 24 })
        throw new InvalidDataException("An IFF ANIM delta frame carries no animation header to say which compression method it uses.");

      var operation = anhd[0];
      var interleave = anhd[18];
      var bits = BinaryPrimitives.ReadUInt32BigEndian(anhd.AsSpan(20));

      if (operation != _OP_BYTE_VERTICAL_DELTA)
        throw new InvalidDataException(
          $"An IFF ANIM delta frame states compression method {operation}. Only method 5 (Byte Vertical "
          + "Delta) is decoded — see this decoder's remarks for why the others are refused.");

      if (interleave is not (0 or 1))
        throw new InvalidDataException(
          $"An IFF ANIM delta frame states interleave {interleave}. Only zero (two frames back) and one "
          + "(the immediately previous frame) are decoded.");

      if (bits != 0)
        throw new InvalidDataException(
          $"An IFF ANIM delta frame's animation header sets option bits {bits:x8}. No measured file sets "
          + "any of them for method 5, so this decoder does not guess at what they would mean.");

      var target = interleave == 1 ? this._current : 1 - this._current;
      var buffer = this._buffers[target] ?? throw new InvalidDataException(
        "An IFF ANIM delta frame targets a buffer no keyframe has ever written to.");

      _ApplyByteVerticalDelta(buffer, dlta, this._planes, this._bytesPerRow, this._planeSize);
      this._current = target;
    } else if (anhd is { Length: >= 1 } && anhd[0] == _OP_DIRECT) {
      // "Set directly" with neither BODY nor DLTA present: the picture is unchanged from whichever
      // buffer was shown last.
    } else {
      throw new InvalidDataException("An IFF ANIM video packet carries neither a picture nor a delta.");
    }

    var current = this._buffers[this._current] ?? throw new InvalidDataException(
      "An IFF ANIM video packet produced no picture and no keyframe has ever established one.");

    frame = this._BuildFrame(current);
    return true;
  }

  private void _ReadBitmapHeader(ReadOnlySpan<byte> bmhd) {
    if (bmhd.Length < 11)
      throw new InvalidDataException("An IFF ANIM BMHD chunk is shorter than the eleven bytes this reads.");

    var width = BinaryPrimitives.ReadUInt16BigEndian(bmhd);
    var height = BinaryPrimitives.ReadUInt16BigEndian(bmhd[2..]);
    var planes = bmhd[8];

    if (width <= 0 || height <= 0)
      throw new InvalidDataException($"An IFF ANIM BMHD chunk states a picture of {width}x{height}, which has no pixels.");

    if (planes is <= 0 or > 8)
      throw new InvalidDataException(
        $"An IFF ANIM BMHD chunk states {planes} bitplanes. Only one to eight are decoded — eight is the "
        + "limit the delta chunk's own pointer table states.");

    this._width = width;
    this._height = height;
    this._planes = planes;
    this._bytesPerRow = (width + 15) / 16 * 2;
    this._planeSize = this._bytesPerRow * height;
    this._hasKeyframe = true;
  }

  /// <summary>PackBits/ByteRun1: a control byte's signed value of zero or more is that many bytes plus
  /// one, copied literally; negative and not -128 is one following byte repeated <c>1 - control</c>
  /// times; exactly -128 is a no-op.</summary>
  private static byte[] _UnpackBody(byte[] body, int compression, int expectedSize) {
    if (compression == 0) {
      if (body.Length < expectedSize)
        throw new InvalidDataException(
          $"An IFF ANIM BODY chunk is {body.Length} bytes, short of the {expectedSize} an uncompressed "
          + "picture of this size and depth needs.");

      var raw = new byte[expectedSize];
      Array.Copy(body, raw, expectedSize);
      return raw;
    }

    if (compression != 1)
      throw new InvalidDataException($"An IFF ANIM BMHD chunk states compression method {compression}, which is not ByteRun1.");

    var result = new byte[expectedSize];
    var at = 0;
    var outAt = 0;
    while (outAt < expectedSize) {
      if (at >= body.Length)
        throw new InvalidDataException("An IFF ANIM BODY chunk's ByteRun1 data ran out before its picture was complete.");

      var control = unchecked((sbyte)body[at++]);
      if (control >= 0) {
        var count = control + 1;
        if (at + count > body.Length || outAt + count > expectedSize)
          throw new InvalidDataException("An IFF ANIM BODY chunk's ByteRun1 literal run runs off the end of the data.");

        Array.Copy(body, at, result, outAt, count);
        at += count;
        outAt += count;
      } else if (control != -128) {
        var count = 1 - control;
        if (at >= body.Length || outAt + count > expectedSize)
          throw new InvalidDataException("An IFF ANIM BODY chunk's ByteRun1 repeat run runs off the end of the data.");

        var value = body[at++];
        for (var i = 0; i < count; ++i)
          result[outAt++] = value;
      }
      // control == -128: no-op, consumes only the control byte itself.
    }

    return result;
  }

  /// <summary>An ILBM's stored order — a scanline's bitplane rows one after another — transposed to
  /// separate, contiguous arrays per plane, which is the layout Byte Vertical Delta's row stride
  /// arithmetic assumes.</summary>
  private static byte[] _InterleavedToPlaneMajor(byte[] interleaved, int planes, int bytesPerRow, int height) {
    var planeSize = bytesPerRow * height;
    var result = new byte[planeSize * planes];
    var scanlineBytes = bytesPerRow * planes;

    for (var y = 0; y < height; ++y) {
      var srcRow = y * scanlineBytes;
      for (var p = 0; p < planes; ++p) {
        var srcOffset = srcRow + p * bytesPerRow;
        var dstOffset = p * planeSize + y * bytesPerRow;
        if (srcOffset + bytesPerRow <= interleaved.Length)
          Array.Copy(interleaved, srcOffset, result, dstOffset, bytesPerRow);
      }
    }

    return result;
  }

  /// <summary>
  /// Jim Kent's byte vertical run-length delta, one bitplane at a time: a plane's byte columns are each
  /// compressed separately, an op-count then that many ops of three kinds — a skip (how many rows to
  /// move forward), a run of literal bytes with the high bit of its own count byte set, or a single byte
  /// repeated a stated number of times — each op advancing the destination one full row (the plane's own
  /// byte width) per byte written or skipped.
  /// </summary>
  private static void _ApplyByteVerticalDelta(byte[] buffer, byte[] dlta, int planes, int bytesPerRow, int planeSize) {
    if (dlta.Length < 32)
      throw new InvalidDataException("An IFF ANIM DLTA chunk is shorter than the thirty-two byte plane pointer table it opens with.");

    for (var p = 0; p < planes; ++p) {
      var pointer = (int)BinaryPrimitives.ReadUInt32BigEndian(dlta.AsSpan(p * 4));
      if (pointer == 0)
        continue;

      if (pointer < 0 || pointer >= dlta.Length)
        throw new InvalidDataException($"An IFF ANIM DLTA chunk's plane {p} pointer names an offset outside the chunk.");

      var planeOffset = p * planeSize;
      var pos = pointer;

      for (var column = 0; column < bytesPerRow; ++column) {
        if (pos >= dlta.Length)
          throw new InvalidDataException("An IFF ANIM DLTA chunk's plane data ran out before every column was accounted for.");

        var opCount = dlta[pos++];
        var destRow = 0;

        for (var op = 0; op < opCount; ++op) {
          if (pos >= dlta.Length)
            throw new InvalidDataException("An IFF ANIM DLTA chunk's plane data ran out mid-column.");

          var opByte = dlta[pos++];
          if (opByte == 0) {
            if (pos + 1 >= dlta.Length)
              throw new InvalidDataException("An IFF ANIM DLTA chunk's 'same' op runs off the end of the data.");

            var count = dlta[pos++];
            var value = dlta[pos++];
            for (var i = 0; i < count; ++i) {
              if (destRow >= _RowsAvailable(planeSize, bytesPerRow))
                throw new InvalidDataException("An IFF ANIM DLTA chunk's 'same' op writes past the bottom of the picture.");

              buffer[planeOffset + destRow * bytesPerRow + column] = value;
              ++destRow;
            }
          } else if ((opByte & 0x80) != 0) {
            var count = opByte & 0x7F;
            if (pos + count > dlta.Length)
              throw new InvalidDataException("An IFF ANIM DLTA chunk's 'uniq' op runs off the end of the data.");

            for (var i = 0; i < count; ++i) {
              if (destRow >= _RowsAvailable(planeSize, bytesPerRow))
                throw new InvalidDataException("An IFF ANIM DLTA chunk's 'uniq' op writes past the bottom of the picture.");

              buffer[planeOffset + destRow * bytesPerRow + column] = dlta[pos++];
              ++destRow;
            }
          } else {
            destRow += opByte;
          }
        }
      }
    }
  }

  private static int _RowsAvailable(int planeSize, int bytesPerRow) => planeSize / bytesPerRow;

  private RawImage _BuildFrame(byte[] planeMajor) {
    if (this._isHam)
      return new() {
        Width = this._width,
        Height = this._height,
        Format = PixelFormat.Rgb24,
        PixelData = _DecodeHam(planeMajor, this._palette, this._width, this._height, this._planes, this._bytesPerRow),
      };

    var indices = _PlaneMajorToChunky(planeMajor, this._width, this._height, this._planes, this._bytesPerRow);
    var paletteCount = this._palette.Length / 3;
    foreach (var index in indices)
      if (index >= paletteCount)
        throw new InvalidDataException(
          $"An IFF ANIM pixel names palette index {index}, which the {paletteCount}-entry palette this "
          + "chunk states does not have.");

    return new() {
      Width = this._width,
      Height = this._height,
      Format = PixelFormat.Indexed8,
      PixelData = indices,
      Palette = this._palette,
      PaletteCount = paletteCount,
    };
  }

  private static byte[] _PlaneMajorToChunky(byte[] planeMajor, int width, int height, int planes, int bytesPerRow) {
    var result = new byte[width * height];
    var planeSize = bytesPerRow * height;

    for (var p = 0; p < planes; ++p) {
      var planeOffset = p * planeSize;
      for (var y = 0; y < height; ++y) {
        var rowOffset = planeOffset + y * bytesPerRow;
        var outRowOffset = y * width;
        for (var x = 0; x < width; ++x) {
          var b = planeMajor[rowOffset + (x >> 3)];
          var bit = b >> (7 - (x & 7)) & 1;
          if (bit != 0)
            result[outRowOffset + x] |= (byte)(1 << p);
        }
      }
    }

    return result;
  }

  /// <summary>Hold-And-Modify: the top two bits of the combined pixel value choose a fresh palette
  /// lookup (zero) or which channel to overwrite in the pixel before it (one blue, two red, three
  /// green), the low bits widened to eight by repeating them. Each row starts from the palette's own
  /// first entry.</summary>
  private static byte[] _DecodeHam(byte[] planeMajor, byte[] palette, int width, int height, int planes, int bytesPerRow) {
    var result = new byte[width * height * 3];
    var planeSize = bytesPerRow * height;
    var controlBits = planes - 2;
    var controlMask = (1 << controlBits) - 1;
    var shift = 8 - controlBits;
    var paletteCount = palette.Length / 3;

    byte[] indices = _PlaneMajorToChunky(planeMajor, width, height, planes, bytesPerRow);
    byte bgR = paletteCount > 0 ? palette[0] : (byte)0;
    byte bgG = paletteCount > 0 ? palette[1] : (byte)0;
    byte bgB = paletteCount > 0 ? palette[2] : (byte)0;

    for (var y = 0; y < height; ++y) {
      byte r = bgR, g = bgG, b = bgB;
      var rowOffset = y * width;

      for (var x = 0; x < width; ++x) {
        var value = indices[rowOffset + x];
        var control = value >> controlBits;
        var low = value & controlMask;
        var widened = (byte)((low << shift | low >> (controlBits - shift)) & 0xFF);

        switch (control) {
          case 0:
            if (low >= paletteCount)
              throw new InvalidDataException(
                $"An IFF ANIM HAM pixel names palette index {low}, which the {paletteCount}-entry "
                + "palette this chunk states does not have.");
            var o = low * 3;
            r = palette[o];
            g = palette[o + 1];
            b = palette[o + 2];
            break;
          case 1: b = widened; break;
          case 2: r = widened; break;
          case 3: g = widened; break;
        }

        var outOffset = (rowOffset + x) * 3;
        result[outOffset] = r;
        result[outOffset + 1] = g;
        result[outOffset + 2] = b;
      }
    }

    return result;
  }
}
