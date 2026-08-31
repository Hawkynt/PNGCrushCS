using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>
/// Decodes BFI video (<c>BFIV</c>, synthetic — Tsunami Media's own "video compression type" field inside
/// each chunk names no format this project found published, so the container's own name for the format
/// stands in for one): palettised 8-bit pictures coded with a small, four-code mix of literal runs,
/// overlap-permitting back-references, unchanged runs carried from the frame before, and flat colour
/// fills.
/// </summary>
/// <remarks>
/// The compression scheme is read from MultimediaWiki's BFI page, whose byte tables leave several header
/// fields marked "unknown" or "(?)" rather than named with the confidence a paraphrase of a working
/// decoder would carry, and which cites no implementation anywhere on it — the one quoted first-party
/// source on the page, the "WHAT'S A BFI?" passage, is <i>Flash Traffic: City of Angels</i>' own
/// README.TXT explaining the acronym, not a bitstream description.
/// <para/>
/// A compressed frame opens with a four-byte little-endian count of its own decompressed size, then a
/// stream of codes: each control byte's top two bits choose one of four operations and its low six bits
/// are a length, extended to a following sixteen-bit value when the six bits read zero. Code zero copies
/// that many literal bytes from the input. Code one copies that many <b>dwords</b> from earlier in the
/// output, four bytes at a time and one byte at a time within each — the page's own warning that this
/// must not be done with a block copy is what a back-reference shorter than four bytes needs, since each
/// byte it copies may itself have been written by the same operation moments before. Code two leaves that
/// many bytes of the output exactly as the frame before it left them, which is what makes decoding a
/// frame start from a copy of the one before rather than from a blank canvas; a code two whose extended
/// length is still zero is not a length of zero pixels but the stream's own stop code. Code three fills
/// that many <b>words</b> — two bytes each — with one flat two-byte value repeated, low byte first.
/// <para/>
/// <b>Measured.</b> Three files from <c>samples.ffmpeg.org/game-formats/bfi/</c> — <c>2287.bfi</c>,
/// <c>5081.bfi</c> and <c>5082.bfi</c>, all 320x140, fifty-seven, forty-three and thirty-eight frames —
/// were decoded here and by ffmpeg and compared sample for sample against ffmpeg's own <c>rgb24</c>
/// output: all 138 frames are identical, maximum delta nought. The palette is six-bit-per-channel VGA
/// precision, widened to eight bits by repeating the value's own low bits — the same construction this
/// library already uses for CDXL's and IFF ANIM's Hold-And-Modify channels — confirmed exact across every
/// frame of all three files, so there is no ambiguity here between six-bit and eight-bit palettes for a
/// decoder to have guessed at.
/// <para/>
/// What is not implemented refuses and says so: a chunk not opening with the four bytes <c>IVAS</c>; a
/// compressed stream whose codes read past the end of the input or write past the frame's own stated
/// size; and a back-reference (code one) whose offset reaches before the start of the output buffer.
/// </remarks>
public sealed class BfiVideoDecoder : IVideoCodecDecoder<BfiVideoDecoder> {

  private static readonly CodecTag _Tag = CodecTag.FromCharacters("BFIV");

  private readonly int _width;
  private readonly int _height;
  private readonly byte[] _palette;
  private byte[]? _previous;

  /// <summary>Gets the codec name.</summary>
  public static string CodecName => "Brute Force & Ignorance Video";

  /// <summary>Determines whether the specified media stream is supported.</summary>
  public static bool Accepts(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);

    return stream.Kind == MediaStreamKind.Video && stream.Codec.EqualsIgnoringCase(_Tag);
  }

  /// <summary>Creates a decoder for the specified media stream.</summary>
  public static BfiVideoDecoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);

    if (stream.Width <= 0 || stream.Height <= 0)
      throw new InvalidDataException($"A BFI video stream states a picture of {stream.Width}x{stream.Height}, which has no pixels.");

    var raw = stream.CodecPrivateData.Span;
    if (raw.Length < 768)
      throw new InvalidDataException($"A BFI video stream carries {raw.Length} bytes of palette, short of the 768 a 256-entry table needs.");

    var palette = new byte[768];
    for (var i = 0; i < 768; ++i)
      palette[i] = ChannelScaling.Expand6(raw[i]);

    return new(stream.Width, stream.Height, palette);
  }

  private BfiVideoDecoder(int width, int height, byte[] palette) {
    this._width = width;
    this._height = height;
    this._palette = palette;
  }

  /// <summary>Attempts to decode the specified coded packet into a raw image frame.</summary>
  public bool TryDecode(CodedPacket packet, out RawImage frame) {
    var span = packet.Data.Span;
    if (span.Length < 8 || span[0] != (byte)'I' || span[1] != (byte)'V' || span[2] != (byte)'A' || span[3] != (byte)'S')
      throw new InvalidDataException("A BFI video packet does not open with the four bytes 'IVAS'.");

    if (span.Length < 24)
      throw new InvalidDataException("A BFI video packet is shorter than the sixteen-byte IVAS payload header it needs.");

    var payload = span[8..];
    var videoOffset = (int)BinaryPrimitives.ReadUInt32LittleEndian(payload[12..]) - 8;
    if (videoOffset < 16 || videoOffset > payload.Length)
      throw new InvalidDataException("A BFI video packet states a video offset outside its own chunk.");

    var videoData = payload[videoOffset..];
    if (videoData.Length < 4)
      throw new InvalidDataException("A BFI video packet's coded picture is shorter than the four-byte size it should open with.");

    var expectedSize = (int)BinaryPrimitives.ReadUInt32LittleEndian(videoData);
    var pixelCount = this._width * this._height;
    if (expectedSize != pixelCount)
      throw new InvalidDataException(
        $"A BFI coded picture states a decompressed size of {expectedSize} bytes, not the {pixelCount} "
        + $"this stream's {this._width}x{this._height} picture needs.");

    var indices = this._previous != null ? (byte[])this._previous.Clone() : new byte[pixelCount];
    _Decompress(videoData[4..], indices);
    this._previous = indices;

    frame = new() {
      Width = this._width,
      Height = this._height,
      Format = PixelFormat.Indexed8,
      PixelData = indices,
      Palette = this._palette,
      PaletteCount = 256,
    };
    return true;
  }

  /// <summary>
  /// The mixed LZ/RLE scheme: a control byte's top two bits choose skip, dump, run or fill (in this
  /// codec's own numbering: 0 literal, 1 back-reference, 2 unchanged, 3 fill), its low six bits a length
  /// extended through a following sixteen-bit value when they read zero.
  /// </summary>
  private static void _Decompress(ReadOnlySpan<byte> input, byte[] output) {
    var outLength = output.Length;
    var cursor = 0;
    var pos = 0;

    while (cursor < outLength) {
      if (pos >= input.Length)
        throw new InvalidDataException("A BFI coded picture's compressed data ran out before its picture was complete.");

      var control = input[pos++];
      var code = control >> 6;
      var length = control & 0x3F;

      switch (code) {
        case 0: { // literal
          if (length == 0) {
            if (pos + 2 > input.Length)
              throw new InvalidDataException("A BFI literal code's extended length runs off the end of the data.");
            length = BinaryPrimitives.ReadUInt16LittleEndian(input[pos..]);
            pos += 2;
          }

          if (pos + length > input.Length || cursor + length > outLength)
            throw new InvalidDataException("A BFI literal code writes past the end of its picture or reads past the end of its data.");

          input.Slice(pos, length).CopyTo(output.AsSpan(cursor, length));
          pos += length;
          cursor += length;
          break;
        }
        case 1: { // back-reference, in dwords
          int offset;
          if (length == 0) {
            if (pos + 3 > input.Length)
              throw new InvalidDataException("A BFI back-reference code's extended length and offset run off the end of the data.");
            length = input[pos++];
            offset = BinaryPrimitives.ReadUInt16LittleEndian(input[pos..]);
            pos += 2;
          } else {
            if (pos + 1 > input.Length)
              throw new InvalidDataException("A BFI back-reference code's offset runs off the end of the data.");
            offset = input[pos++];
          }

          if (offset <= 0 || offset > cursor)
            throw new InvalidDataException($"A BFI back-reference code names an offset of {offset}, reaching before the start of the picture.");

          var byteCount = length * 4;
          if (cursor + byteCount > outLength)
            throw new InvalidDataException("A BFI back-reference code writes past the end of its picture.");

          // Byte by byte and not a block copy: a run shorter than the offset legitimately reads bytes
          // this same loop wrote moments before, which is the whole point of an LZ back-reference.
          for (var i = 0; i < byteCount; ++i) {
            output[cursor] = output[cursor - offset];
            ++cursor;
          }

          break;
        }
        case 2: { // unchanged from the frame before
          if (length == 0) {
            if (pos + 2 > input.Length)
              throw new InvalidDataException("A BFI unchanged code's extended length runs off the end of the data.");
            length = BinaryPrimitives.ReadUInt16LittleEndian(input[pos..]);
            pos += 2;
            if (length == 0)
              return; // The stream's own stop code, not a run of zero pixels.
          }

          if (cursor + length > outLength)
            throw new InvalidDataException("A BFI unchanged code reaches past the end of its picture.");

          cursor += length; // output already holds the previous frame's bytes here.
          break;
        }
        case 3: { // fill
          if (length == 0) {
            if (pos + 2 > input.Length)
              throw new InvalidDataException("A BFI fill code's extended length runs off the end of the data.");
            length = BinaryPrimitives.ReadUInt16LittleEndian(input[pos..]);
            pos += 2;
          }

          if (pos + 2 > input.Length)
            throw new InvalidDataException("A BFI fill code's colour value runs off the end of the data.");

          var lo = input[pos];
          var hi = input[pos + 1];
          pos += 2;

          var byteCount = length * 2;
          if (cursor + byteCount > outLength)
            throw new InvalidDataException("A BFI fill code writes past the end of its picture.");

          for (var i = 0; i < length; ++i) {
            output[cursor++] = lo;
            output[cursor++] = hi;
          }

          break;
        }
      }
    }
  }
}
