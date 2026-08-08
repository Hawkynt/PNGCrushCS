using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.Core;
using FileFormat.Jpeg;

namespace FileFormat.Kqp;

/// <summary>Writes a Konica Quality Photo picture: the bitmap headers, the palette, then the table-less JPEG.</summary>
/// <remarks>
/// The scan has to be quantised against the very tables the file leaves out — the same constant the
/// reader puts back — or nothing decodes it to the picture that went in, this reader included. So
/// the JPEG is encoded with those tables and with the standard Huffman tables of Annex K, and both
/// sets are then cut out of the stream, which is what makes it a Konica file rather than an ordinary
/// JPEG with a bitmap header in front.
/// <para/>
/// Everything else follows the six samples, which agree with each other field for field: a
/// sixty-eight byte info header stating twenty-four bits and a compression of <c>JPEG</c>, a height
/// written negative because the rows run top-down, a palette of two hundred and fifty-two entries,
/// and the private <c>PIC</c> segment inside the JPEG. The file size field is nought in all six and
/// is written nought here too.
/// </remarks>
public static class KqpWriter {

  /// <summary>How many palette entries every sample carries, and what its two colour counts state.</summary>
  private const int _PaletteEntries = 252, _ColoursUsed = 252, _ColoursImportant = 236;

  /// <summary>The seven longs Konica put after the ordinary info header, identical in every sample.</summary>
  private static ReadOnlySpan<int> _Extra => [44, 24, 0, 2, 8, 1, 1];

  /// <summary>Konica's private segment, which every sample carries between the JFIF one and the frame.</summary>
  private static ReadOnlySpan<byte> _PicSegment => [0xFF, 0xE1, 0x00, 0x0A, 0x50, 0x49, 0x43, 0x00, 0x01, 0x0E, 0x0E, 0x01];

  /// <summary>Markers whose segments this format stores nothing of.</summary>
  private const byte _DefineQuantisationTables = 0xDB, _DefineHuffmanTables = 0xC4, _StartOfScan = 0xDA, _StartOfFrame = 0xC0;

  public static byte[] ToBytes(KqpFile file) {
    var width = file.Width;
    var height = file.Height;
    if (width < 1 || height < 1)
      throw new ArgumentException($"Invalid KQP picture size: {width}x{height}.", nameof(file));

    var pixels = file.PixelData ?? new byte[width * height * 3];
    if (pixels.Length < width * height * 3)
      throw new ArgumentException($"A KQP picture of {width} by {height} needs {width * height * 3} bytes and has {pixels.Length}.", nameof(file));

    var jpeg = _Encode(pixels, width, height);
    var palette = _Palette(pixels, width, height);
    var offset = KqpFile.FileHeaderSize + KqpFile.InfoHeaderSize + palette.Length;

    var result = new byte[offset + jpeg.Length];
    KqpFile.Magic.CopyTo(result);

    // The size field is nought in every sample; the reader accounts for the file from the offset and
    // the JPEG instead, so writing a length here would be inventing a field the format does not use.
    BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(KqpFile.DataOffsetField), offset);

    var info = result.AsSpan(KqpFile.FileHeaderSize);
    BinaryPrimitives.WriteInt32LittleEndian(info, KqpFile.InfoHeaderSize);
    BinaryPrimitives.WriteInt32LittleEndian(info[4..], width);
    BinaryPrimitives.WriteInt32LittleEndian(info[8..], -height);
    BinaryPrimitives.WriteInt16LittleEndian(info[12..], 1);
    BinaryPrimitives.WriteInt16LittleEndian(info[14..], 24);
    KqpFile.JpegCompression.CopyTo(info[16..]);
    BinaryPrimitives.WriteInt32LittleEndian(info[32..], _ColoursUsed);
    BinaryPrimitives.WriteInt32LittleEndian(info[36..], _ColoursImportant);
    for (var i = 0; i < _Extra.Length; ++i)
      BinaryPrimitives.WriteInt32LittleEndian(info[(40 + i * 4)..], _Extra[i]);

    palette.CopyTo(result, KqpFile.FileHeaderSize + KqpFile.InfoHeaderSize);
    jpeg.CopyTo(result, offset);

    return result;
  }

  /// <summary>The picture's own colours, as the four-byte entries a bitmap palette holds.</summary>
  private static byte[] _Palette(byte[] pixels, int width, int height) {
    var reduced = new RawImage { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = pixels }
      .EnsureIndexedAtMost(_PaletteEntries);

    var source = reduced.Palette ?? [];
    var entries = new byte[_PaletteEntries * 4];
    for (var i = 0; i < _PaletteEntries && i * 3 + 2 < source.Length; ++i) {
      entries[i * 4] = source[i * 3 + 2];
      entries[i * 4 + 1] = source[i * 3 + 1];
      entries[i * 4 + 2] = source[i * 3];
    }

    return entries;
  }

  /// <summary>Encodes the JPEG this format stores, with the tables it omits taken back out again.</summary>
  private static byte[] _Encode(byte[] pixels, int width, int height) {
    var complete = JpegManagedEncoder.Encode(
      pixels, width, height,
      quality: 90, JpegMode.Baseline, JpegSubsampling.Chroma444,
      optimizeHuffman: false, isGrayscale: false,
      quantTablesNatural: [_Natural(0), _Natural(1)]);

    using var stripped = new MemoryStream();
    stripped.Write(complete.AsSpan(0, 2));

    var at = 2;
    while (at + 4 <= complete.Length) {
      var marker = complete[at + 1];
      if (marker == _StartOfScan) {
        stripped.Write(complete.AsSpan(at));
        break;
      }

      var length = 2 + BinaryPrimitives.ReadUInt16BigEndian(complete.AsSpan(at + 2));

      // Konica's own segment sits directly before the frame header in every sample.
      if (marker == _StartOfFrame)
        stripped.Write(_PicSegment);

      if (marker is not (_DefineQuantisationTables or _DefineHuffmanTables))
        stripped.Write(complete.AsSpan(at, length));

      at += length;
    }

    return stripped.ToArray();
  }

  /// <summary>One of the stored tables in natural order, which is what quantising wants.</summary>
  /// <remarks>
  /// The constant is a ready-made segment, and a segment holds its coefficients in zigzag order. Read
  /// straight through it would put every coefficient but the first in the wrong place, which is the
  /// sort of mistake a round trip through this same pair of tables would never show.
  /// </remarks>
  private static int[] _Natural(int table) {
    // Four bytes of marker and length, then for each table an identifier byte and its coefficients.
    var at = 4 + table * 65 + 1;
    var natural = new int[64];
    for (var k = 0; k < 64; ++k)
      natural[JpegZigZag.Order[k]] = KqpFile.QuantisationTables[at + k];

    return natural;
  }
}
