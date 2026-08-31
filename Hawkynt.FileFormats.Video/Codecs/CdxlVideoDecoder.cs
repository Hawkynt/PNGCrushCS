using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>
/// Decodes Commodore CDXL video (<c>CDXL</c>): the Amiga CDTV's own uncompressed bit-planar picture,
/// either read straight through a twelve-bit palette (RGB) or through Hold-And-Modify (HAM6), where
/// most of a picture is coded as which of the previous pixel's three channels to overwrite rather than
/// as a colour of its own.
/// </summary>
/// <remarks>
/// Every packet carries a complete chunk — the thirty-two byte header <see cref="Formats.Cdxl.CdxlContainer"/>
/// demuxed rather than stripped, followed by that frame's own palette and pixel bytes — because CDXL
/// states its width, height, plane count and encoding freshly in every chunk rather than once for the
/// whole stream, and later, AGA-era extensions of the format are documented as varying some of those
/// from one chunk to the next. Reading each packet's own header is what lets this decoder mean the same
/// thing for both cases without a container having to know which one a file uses.
/// <para/>
/// <b>Bitplanes are plane-major.</b> Plane arrangement zero — the only one this decoder or <see
/// cref="Formats.Cdxl.CdxlContainer"/> reads, see that type's remarks — lays out all of bitplane zero
/// top to bottom, then all of bitplane one, and so on; each row is <c>ceil(width / 8)</c> bytes with the
/// leftmost pixel in the most significant bit of the first byte. Plane zero contributes bit zero of the
/// combined pixel value and the last plane contributes the most significant bit. Confirmed against
/// ffmpeg's own decode of four real files at four, six and eight planes with no differing sample.
/// <para/>
/// <b>The palette is twelve-bit RGB, four bits a channel, widened to eight bits by repeating the nibble</b>
/// — the same construction this library's IFF ILBM reader uses for the same Amiga 12-bit colour words —
/// confirmed exact against all four files' RGB-encoded frames.
/// <para/>
/// <b>Hold-And-Modify.</b> The top two bits of the combined pixel value choose what a pixel means: zero
/// is a fresh palette lookup through the low bits as an index; one, two and three hold the pixel before
/// it and overwrite blue, red or green respectively with the low bits widened to eight. Only HAM6 — six
/// bitplanes, a four-bit index and a four-bit modify value — was measured, and there the four-bit modify
/// value is widened the same way the twelve-bit palette is, by repeating its own nibble
/// (<c>(value &lt;&lt; 4) | value</c>): a sixteen-frame real file, cross-checked pixel by pixel including
/// every odd value the widened byte could disagree with plain shifting on, came back with no differing
/// sample. <b>Each row starts from the palette's own first entry, not from black</b> — an Amiga display
/// carries a border colour into the first pixels of a line before anything modifies them, which one file
/// showed as a small, constant discrepancy in exactly the rows whose first coded pixel is a modify
/// opcode rather than a fresh lookup, and which starting from palette index zero instead removes
/// entirely.
/// <para/>
/// <b>HAM8 does not reach the same bar and is refused.</b> A real eight-bitplane HAM file's blue and
/// green channels decode exactly with the same widening rule scaled to six modify bits and two control
/// bits (<c>value &lt;&lt; 2</c>, without the wraparound replication HAM6 needs — confirmed separately,
/// since the two channels already agree exactly without it and adding it disagrees). Its red channel does
/// not: repeated measurement against the same real file found errors of one to three levels on about a
/// third of that channel's modify opcodes, with no formula in the modify value alone accounting for them
/// — the same value produces a different error at different positions in the picture, which rules out a
/// scaling mistake and points at either a genuinely different rule this investigation did not find or an
/// inconsistency in the oracle's own red channel. Either way, this decoder refuses HAM8 by name rather
/// than ship a picture with an unexplained wrong channel in it.
/// <para/>
/// <b>Measured.</b> Four files from <c>samples.ffmpeg.org/cdxl/</c> — <c>cat.cdxl</c> (160x120, six
/// planes, HAM), <c>fruit.cdxl</c> (128x80, four planes, RGB), <c>maku.cdxl</c> (176x128, eight planes,
/// RGB) and <c>mirage.cdxl</c> (176x128, eight planes, HAM) — were decoded here and by ffmpeg and
/// compared sample for sample against ffmpeg's own <c>rgb24</c> output, RGB-native so there is no
/// chroma-siting convention to disagree about. Every frame of the RGB files and every frame of the HAM6
/// file (forty-two frames of RGB across two files, sixteen of HAM6) is identical, maximum delta nought.
/// <c>mirage.cdxl</c>'s HAM8 frames are the one case this decoder refuses rather than ships, for the
/// reason above.
/// <para/>
/// <b>What is not implemented refuses and says so:</b> a plane arrangement other than bit planar; the YUV
/// and AVM/DCTV video encodings CDXL's own documentation names but no measured file uses; HAM at any
/// plane count other than six; a packet too short to carry its own stated palette and pixel bytes; and a
/// palette index a coded pixel names that the stated palette does not have an entry for.
/// </remarks>
public sealed class CdxlVideoDecoder : IVideoCodecDecoder<CdxlVideoDecoder> {

  private const int _HEADER_LENGTH = 32;
  private const int _ENCODING_RGB = 0;
  private const int _ENCODING_HAM = 1;
  private const int _HAM6_PLANES = 6;

  private static readonly CodecTag _Tag = CodecTag.FromCharacters("CDXL");

  /// <summary>Gets the codec name.</summary>
  public static string CodecName => "Commodore CDXL Video";

  /// <summary>Determines whether the specified media stream is supported.</summary>
  public static bool Accepts(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);

    return stream.Kind == MediaStreamKind.Video && stream.Codec.EqualsIgnoringCase(_Tag);
  }

  /// <summary>Creates a decoder for the specified media stream.</summary>
  public static CdxlVideoDecoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);

    return new();
  }

  private CdxlVideoDecoder() { }

  /// <summary>Attempts to decode the specified coded packet into a raw image frame.</summary>
  public bool TryDecode(CodedPacket packet, out RawImage frame) {
    var data = packet.Data.Span;
    if (data.Length < _HEADER_LENGTH)
      throw new InvalidDataException(
        $"A CDXL video packet is {data.Length} bytes, short of the thirty-two byte header every chunk "
        + "opens with.");

    var info = data[1];
    var videoEncoding = info & 0x07;
    var planeArrangement = info >> 5 & 0x07;
    var width = BinaryPrimitives.ReadUInt16BigEndian(data[14..]);
    var height = BinaryPrimitives.ReadUInt16BigEndian(data[16..]);
    var planes = BinaryPrimitives.ReadUInt16BigEndian(data[18..]);
    var paletteSize = BinaryPrimitives.ReadUInt16BigEndian(data[20..]);

    if (planeArrangement != 0)
      throw new InvalidDataException(
        $"A CDXL video packet states plane arrangement {planeArrangement}, not the bit planar layout "
        + "(zero) this decoder was measured against.");

    if (videoEncoding is not (_ENCODING_RGB or _ENCODING_HAM))
      throw new InvalidDataException(
        $"A CDXL video packet states video encoding {videoEncoding} — YUV or AVM/DCTV — which no file "
        + "this decoder was measured against uses.");

    if (videoEncoding == _ENCODING_HAM && planes != _HAM6_PLANES)
      throw new InvalidDataException(
        $"A CDXL video packet states Hold-And-Modify at {planes} bitplanes. Only HAM6 (six bitplanes) "
        + "reaches this decoder's own bar against ffmpeg's decode — see the type's remarks for HAM8's "
        + "unresolved red channel.");

    if (width <= 0 || height <= 0)
      throw new InvalidDataException($"A CDXL video packet states a picture of {width}x{height}, which has no pixels.");

    if (planes <= 0 || planes > 32)
      throw new InvalidDataException($"A CDXL video packet states {planes} bitplanes.");

    var bytesPerRow = (width + 7) / 8;
    var pixelBytes = bytesPerRow * height * planes;
    var paletteCount = paletteSize / 2;

    if (data.Length < _HEADER_LENGTH + paletteSize + pixelBytes)
      throw new InvalidDataException(
        "A CDXL video packet is shorter than its own header states its palette and pixel bytes need.");

    var palette = _ReadPalette(data.Slice(_HEADER_LENGTH, paletteSize), paletteCount);
    var indices = _ReadBitplanes(data.Slice(_HEADER_LENGTH + paletteSize, pixelBytes), width, height, planes, bytesPerRow);

    var rgb = videoEncoding == _ENCODING_HAM
      ? _DecodeHam6(indices, palette, width, height)
      : _DecodeRgb(indices, palette, width, height, paletteCount);

    frame = new() {
      Width = width,
      Height = height,
      Format = PixelFormat.Rgb24,
      PixelData = rgb,
    };
    return true;
  }

  /// <summary>Twelve-bit RGB, four bits a channel in a big-endian word's low twelve bits, widened to
  /// eight bits by repeating the nibble — <c>0x0RGB</c>, the same layout this library's IFF ILBM reader
  /// widens the same way.</summary>
  private static byte[][] _ReadPalette(ReadOnlySpan<byte> data, int count) {
    var palette = new byte[count][];
    for (var i = 0; i < count; ++i) {
      var word = BinaryPrimitives.ReadUInt16BigEndian(data[(i * 2)..]);
      var r = (word >> 8) & 0x0F;
      var g = (word >> 4) & 0x0F;
      var b = word & 0x0F;
      palette[i] = [(byte)((r << 4) | r), (byte)((g << 4) | g), (byte)((b << 4) | b)];
    }

    return palette;
  }

  /// <summary>
  /// Plane-major bit planar: all of bitplane zero top to bottom, then all of bitplane one, and so on,
  /// each row packed eight pixels to a byte with the leftmost pixel in the most significant bit. Plane
  /// zero contributes bit zero of the combined pixel value.
  /// </summary>
  private static int[] _ReadBitplanes(ReadOnlySpan<byte> data, int width, int height, int planes, int bytesPerRow) {
    var result = new int[width * height];
    var planeSize = bytesPerRow * height;

    for (var p = 0; p < planes; ++p) {
      var planeOffset = p * planeSize;
      for (var y = 0; y < height; ++y) {
        var rowOffset = planeOffset + y * bytesPerRow;
        var outRowOffset = y * width;
        for (var x = 0; x < width; ++x) {
          var b = data[rowOffset + (x >> 3)];
          var bit = b >> (7 - (x & 7)) & 1;
          if (bit != 0)
            result[outRowOffset + x] |= 1 << p;
        }
      }
    }

    return result;
  }

  private static byte[] _DecodeRgb(int[] indices, byte[][] palette, int width, int height, int paletteCount) {
    var result = new byte[width * height * 3];
    for (var i = 0; i < indices.Length; ++i) {
      var index = indices[i];
      if (index >= paletteCount)
        throw new InvalidDataException(
          $"A CDXL RGB pixel names palette index {index}, which the {paletteCount}-entry palette this "
          + "chunk states does not have.");

      var colour = palette[index];
      var o = i * 3;
      result[o] = colour[0];
      result[o + 1] = colour[1];
      result[o + 2] = colour[2];
    }

    return result;
  }

  /// <summary>
  /// Hold-And-Modify at six bitplanes: the top two bits of the combined pixel value choose a fresh
  /// palette lookup (zero) or which channel to overwrite in the pixel before it (one blue, two red,
  /// three green), the low four bits widened to eight by repeating the nibble. Each row starts from the
  /// palette's own first entry — see the type's remarks for why black is the wrong starting colour.
  /// </summary>
  private static byte[] _DecodeHam6(int[] indices, byte[][] palette, int width, int height) {
    var result = new byte[width * height * 3];
    var background = palette.Length > 0 ? palette[0] : [(byte)0, (byte)0, (byte)0];

    for (var y = 0; y < height; ++y) {
      byte r = background[0], g = background[1], b = background[2];
      var rowOffset = y * width;

      for (var x = 0; x < width; ++x) {
        var value = indices[rowOffset + x];
        var control = value >> 4;
        var low = value & 0x0F;
        var widened = (byte)((low << 4) | low);

        switch (control) {
          case 0:
            if (low >= palette.Length)
              throw new InvalidDataException(
                $"A CDXL HAM6 pixel names palette index {low}, which the {palette.Length}-entry palette "
                + "this chunk states does not have.");
            var colour = palette[low];
            r = colour[0];
            g = colour[1];
            b = colour[2];
            break;
          case 1:
            b = widened;
            break;
          case 2:
            r = widened;
            break;
          case 3:
            g = widened;
            break;
        }

        var o = (rowOffset + x) * 3;
        result[o] = r;
        result[o + 1] = g;
        result[o + 2] = b;
      }
    }

    return result;
  }
}
