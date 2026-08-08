using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.Core;
using FileFormat.Jpeg;

namespace FileFormat.Kqp;

/// <summary>Reads Konica Quality Photo pictures from bytes, streams, or file paths.</summary>
public static class KqpReader {

  /// <summary>
  /// The quantisation tables every one of these is coded against, as a ready-made segment.
  /// </summary>
  /// <remarks>
  /// Eight-bit precision, two tables: nought for luminance and one for both chrominance components,
  /// which is what the scan header asks for. The camera never wrote them into the file, so a decoder
  /// has to know them; these are the ones the Konica software used.
  /// </remarks>
  private static ReadOnlySpan<byte> _QuantisationTables => [
    0xFF, 0xDB, 0x00, 0x84,
    0x00,
    0x0C, 0x08, 0x09, 0x0A, 0x09, 0x07, 0x0C, 0x0A,
    0x0A, 0x0A, 0x0E, 0x0D, 0x0C, 0x0E, 0x12, 0x1F,
    0x14, 0x12, 0x11, 0x11, 0x12, 0x26, 0x1B, 0x1C,
    0x16, 0x1F, 0x2D, 0x27, 0x2F, 0x2E, 0x2C, 0x27,
    0x2B, 0x2A, 0x32, 0x38, 0x47, 0x3C, 0x32, 0x35,
    0x43, 0x35, 0x2A, 0x2B, 0x3E, 0x55, 0x3F, 0x43,
    0x4A, 0x4C, 0x50, 0x51, 0x50, 0x30, 0x3C, 0x58,
    0x5E, 0x57, 0x4E, 0x5D, 0x47, 0x4E, 0x50, 0x4D,
    0x01,
    0x0F, 0x10, 0x10, 0x16, 0x13, 0x16, 0x2C, 0x18,
    0x18, 0x2C, 0x5C, 0x3D, 0x34, 0x3D, 0x5C, 0x5C,
    0x5C, 0x5C, 0x5C, 0x5C, 0x5C, 0x5C, 0x5C, 0x5C,
    0x5C, 0x5C, 0x5C, 0x5C, 0x5C, 0x5C, 0x5C, 0x5C,
    0x5C, 0x5C, 0x5C, 0x5C, 0x5C, 0x5C, 0x5C, 0x5C,
    0x5C, 0x5C, 0x5C, 0x5C, 0x5C, 0x5C, 0x5C, 0x5C,
    0x5C, 0x5C, 0x5C, 0x5C, 0x5C, 0x5C, 0x5C, 0x5C,
    0x5C, 0x5C, 0x5C, 0x5C, 0x5C, 0x5C, 0x5C, 0x5C,
  ];

  public static KqpFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("KQP picture not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static KqpFile FromStream(Stream stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.CanSeek) {
      var data = new byte[stream.Length - stream.Position];
      stream.ReadExactly(data);
      return FromBytes(data);
    }

    using var ms = new MemoryStream();
    stream.CopyTo(ms);
    return FromBytes(ms.ToArray());
  }

  public static KqpFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < KqpFile.FileHeaderSize + KqpFile.InfoHeaderSize || !data[..2].SequenceEqual(KqpFile.Magic))
      throw new InvalidDataException("Not a KQP picture: it does not open with BM.");

    // An ordinary bitmap states forty here and a compression that is a small number. Sixty-eight and
    // four letters together are what say this is Konica's variant rather than a bitmap we already
    // read elsewhere.
    var infoHeader = BinaryPrimitives.ReadInt32LittleEndian(data[KqpFile.FileHeaderSize..]);
    if (infoHeader != KqpFile.InfoHeaderSize)
      throw new InvalidDataException($"A KQP info header is {KqpFile.InfoHeaderSize} bytes and this one states {infoHeader}.");

    if (!data.Slice(KqpFile.FileHeaderSize + 16, 4).SequenceEqual(KqpFile.JpegCompression))
      throw new InvalidDataException("A KQP picture states a compression of JPEG and this one does not.");

    var width = BinaryPrimitives.ReadInt32LittleEndian(data[(KqpFile.FileHeaderSize + 4)..]);
    var storedHeight = BinaryPrimitives.ReadInt32LittleEndian(data[(KqpFile.FileHeaderSize + 8)..]);
    var height = Math.Abs(storedHeight);
    if (width < 1 || height < 1)
      throw new InvalidDataException($"A KQP picture states a size of {width} by {storedHeight}.");

    var offset = BinaryPrimitives.ReadInt32LittleEndian(data[KqpFile.DataOffsetField..]);
    if (offset < KqpFile.FileHeaderSize + KqpFile.InfoHeaderSize || offset >= data.Length)
      throw new InvalidDataException($"A KQP picture states its data at {offset} and is {data.Length} bytes.");

    var stream = data[offset..];
    if (stream.Length < 4 || stream[0] != 0xFF || stream[1] != 0xD8)
      throw new InvalidDataException("A KQP picture states a JPEG at its data offset and there is none.");

    var image = JpegFile.ToRawImage(JpegReader.FromBytes(_CompleteJpeg(stream)));
    var rgb = PixelConverter.Convert(image, PixelFormat.Rgb24);

    // The bitmap header and the JPEG's own frame header both state the size, and they agree in every
    // one of these. Taking the JPEG's would hide a file where they did not.
    if (rgb.Width != width || rgb.Height != height)
      throw new InvalidDataException($"A KQP picture states {width} by {height} and its JPEG is {rgb.Width} by {rgb.Height}.");

    return new() {
      Width = rgb.Width,
      Height = rgb.Height,
      PixelData = rgb.PixelData,
    };
  }

  public static KqpFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }

  /// <summary>The stored stream with the tables it omits put back in front of its scan.</summary>
  private static byte[] _CompleteJpeg(ReadOnlySpan<byte> stream) {
    var scan = _FindScanHeader(stream);

    var tables = new MemoryStream();
    tables.Write(_QuantisationTables);
    _WriteHuffmanTable(tables, 0x00, JpegStandardTables.DcLuminanceBits, JpegStandardTables.DcLuminanceValues);
    _WriteHuffmanTable(tables, 0x10, JpegStandardTables.AcLuminanceBits, JpegStandardTables.AcLuminanceValues);
    _WriteHuffmanTable(tables, 0x01, JpegStandardTables.DcChrominanceBits, JpegStandardTables.DcChrominanceValues);
    _WriteHuffmanTable(tables, 0x11, JpegStandardTables.AcChrominanceBits, JpegStandardTables.AcChrominanceValues);

    var inserted = tables.ToArray();
    var result = new byte[stream.Length + inserted.Length];
    stream[..scan].CopyTo(result);
    inserted.CopyTo(result.AsSpan(scan));
    stream[scan..].CopyTo(result.AsSpan(scan + inserted.Length));

    return result;
  }

  /// <summary>Where the scan header starts, which is where the missing tables have to go.</summary>
  private static int _FindScanHeader(ReadOnlySpan<byte> stream) {
    for (var at = 2; at + 4 <= stream.Length;) {
      if (stream[at] != 0xFF)
        throw new InvalidDataException($"A KQP picture's JPEG has no marker at {at}.");

      var marker = stream[at + 1];
      if (marker == 0xDA)
        return at;

      // Any table the file does carry would make this reader's copies a duplicate definition, and
      // none of these carries one; a file that did is not the format described here.
      if (marker is 0xDB or 0xC4)
        throw new InvalidDataException("A KQP picture is stored without its tables and this one carries them.");

      var length = BinaryPrimitives.ReadUInt16BigEndian(stream[(at + 2)..]);
      if (length < 2)
        throw new InvalidDataException($"A KQP picture's JPEG states a {length} byte segment at {at}.");

      at += 2 + length;
    }

    throw new InvalidDataException("A KQP picture's JPEG has no scan header.");
  }

  private static void _WriteHuffmanTable(Stream target, byte identifier, byte[] bits, byte[] values) {
    var length = 2 + 1 + bits.Length + values.Length;
    target.WriteByte(0xFF);
    target.WriteByte(0xC4);
    target.WriteByte((byte)(length >> 8));
    target.WriteByte((byte)length);
    target.WriteByte(identifier);
    target.Write(bits);
    target.Write(values);
  }
}
