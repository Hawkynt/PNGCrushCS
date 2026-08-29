using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;
using FileFormat.Core;
using FileFormat.Png;

namespace FileFormat.Xar;

/// <summary>Writes a standards-conforming Xara web document containing one bitmap object.</summary>
/// <remarks>
/// This is deliberately a real document, not a thumbnail-only XAR. The open XAR specification
/// permits web files to omit the paper-document framework records. The writer therefore emits the
/// compulsory file header, a PNG bitmap definition, the bitmap-properties record, explicit no-fill
/// and no-line attributes, a <c>TAG_NODE_BITMAP</c> object that references the definition, and the
/// compulsory end-of-file record. The bitmap occupies a rectangle whose physical size is chosen so
/// one source pixel corresponds to one 96-DPI display pixel.
/// </remarks>
public static class XarWriter {

  private const int _MillipointsPerPixelAt96Dpi = 750; // 72000 millipoints / 96 pixels per inch

  public static byte[] ToBytes(XarFile file) {
    var image = file.Bitmap ?? file.Preview
      ?? throw new ArgumentException("A XAR document needs a bitmap to write.", nameof(file));

    if (image.Width <= 0 || image.Height <= 0)
      throw new ArgumentException("A XAR bitmap needs positive dimensions.", nameof(file));

    var width = checked(image.Width * _MillipointsPerPixelAt96Dpi);
    var height = checked(image.Height * _MillipointsPerPixelAt96Dpi);
    var png = PngWriter.ToBytes(PngFile.FromRawImage(image));

    using var output = new MemoryStream();
    output.Write(XarFile.Magic);

    _WriteRecord(output, XarFile.TagFileHeader, _FileHeader());                    // sequence 1
    _WriteRecord(output, XarFile.TagDefineBitmapPngReal, _BitmapDefinition(png)); // sequence 2
    _WriteRecord(output, XarFile.TagBitmapProperties, _BitmapProperties(2));       // sequence 3
    _WriteRecord(output, XarFile.TagFlatFillNone, []);                             // sequence 4
    _WriteRecord(output, XarFile.TagLineColourNone, []);                           // sequence 5
    _WriteRecord(output, XarFile.TagNodeBitmap, _BitmapNode(width, height, 2));     // sequence 6
    _WriteRecord(output, XarFile.TagEndOfFile, []);                                // sequence 7

    return output.ToArray();
  }

  private static byte[] _FileHeader() {
    using var stream = new MemoryStream();
    stream.Write("CXW"u8); // web file: document/chapter/spread/page/layer framework is optional
    _WriteUInt32(stream, 0); // uncompressed-size estimate may be zero
    _WriteUInt32(stream, 0); // reserved web link
    _WriteUInt32(stream, 0); // no refinement
    _WriteAsciiZ(stream, "PNGCrushCS");
    _WriteAsciiZ(stream, "1");
    _WriteAsciiZ(stream, "1");
    return stream.ToArray();
  }

  private static byte[] _BitmapDefinition(byte[] png) {
    // Bitmap names are Unicode strings in the current XAR specification. An empty name is the
    // recommended representation for web-only files, so it is just a UTF-16 NUL before the PNG.
    var data = new byte[checked(2 + png.Length)];
    png.CopyTo(data, 2);
    return data;
  }

  private static byte[] _BitmapProperties(uint bitmapReference) {
    var data = new byte[12];
    BinaryPrimitives.WriteUInt32LittleEndian(data, bitmapReference);
    data[4] = 1; // use interpolation when scaling
    return data;
  }

  private static byte[] _BitmapNode(int width, int height, uint bitmapReference) {
    var data = new byte[36];
    _Coordinate(data, 0, 0, 0);          // bottom-left
    _Coordinate(data, 8, width, 0);      // bottom-right
    _Coordinate(data, 16, width, height);// top-right
    _Coordinate(data, 24, 0, height);    // top-left
    BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(32), bitmapReference);
    return data;
  }

  private static void _Coordinate(byte[] data, int offset, int x, int y) {
    BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(offset), x);
    BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(offset + 4), y);
  }

  private static void _WriteRecord(Stream output, uint tag, ReadOnlySpan<byte> payload) {
    Span<byte> header = stackalloc byte[XarFile.RecordHeaderSize];
    BinaryPrimitives.WriteUInt32LittleEndian(header, tag);
    BinaryPrimitives.WriteUInt32LittleEndian(header[4..], checked((uint)payload.Length));
    output.Write(header);
    output.Write(payload);
  }

  private static void _WriteUInt32(Stream stream, uint value) {
    Span<byte> data = stackalloc byte[4];
    BinaryPrimitives.WriteUInt32LittleEndian(data, value);
    stream.Write(data);
  }

  private static void _WriteAsciiZ(Stream stream, string value) {
    stream.Write(Encoding.ASCII.GetBytes(value));
    stream.WriteByte(0);
  }
}
