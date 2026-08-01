using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;

namespace FileFormat.MatLab;

/// <summary>Assembles a MATLAB Level 5 file holding one array.</summary>
/// <remarks>
/// The previous writer put a width and height in the first bytes of the description text and the
/// pixels straight after the header, which is not this format at all — it matched only the reader
/// that had invented the same layout. What the format wants is a chain of tagged elements, each
/// stating its own type and length and padded out to a multiple of eight.
/// </remarks>
public static class MatLabWriter {

  private const int _Matrix = 14;
  private const int _UInt8 = 2;
  private const int _Int8 = 1;
  private const int _Int32 = 5;
  private const int _UInt32 = 6;

  /// <summary>The class number an array of bytes carries.</summary>
  private const byte _UInt8Class = 9;

  /// <summary>The name the array is stored under, since it must have one.</summary>
  /// <remarks>
  /// One character, so the element fits the short form. Readers of this format are not all general
  /// walkers of the tag chain — some read the elements at the offsets they expect them to be at, and
  /// a longer name in the long form pushes everything after it out of place.
  /// </remarks>
  private const string _ArrayName = "M";

  public static byte[] ToBytes(MatLabFile file) {
    ArgumentNullException.ThrowIfNull(file);

    int width = file.Width, height = file.Height;
    var pixels = file.PixelData ?? new byte[width * height * 3];

    using var array = new MemoryStream();

    // Flags: what kind of array this is. The rest of the word is unset for a plain one.
    _WriteElement(array, _UInt32, [_UInt8Class, 0, 0, 0, 0, 0, 0, 0]);

    // Shape, rows first, with the colour planes last.
    var shape = new byte[12];
    BinaryPrimitives.WriteInt32LittleEndian(shape, height);
    BinaryPrimitives.WriteInt32LittleEndian(shape.AsSpan(4), width);
    BinaryPrimitives.WriteInt32LittleEndian(shape.AsSpan(8), 3);
    _WriteElement(array, _Int32, shape);

    _WriteShortElement(array, _Int8, Encoding.ASCII.GetBytes(_ArrayName));

    // The values, one whole plane at a time and each plane read down its columns.
    var plane = width * height;
    var values = new byte[plane * 3];
    for (var channel = 0; channel < 3; ++channel)
    for (var x = 0; x < width; ++x)
    for (var y = 0; y < height; ++y) {
      var source = (y * width + x) * 3 + channel;
      values[channel * plane + x * height + y] = source < pixels.Length ? pixels[source] : (byte)0;
    }

    _WriteElement(array, _UInt8, values);

    using var output = new MemoryStream();
    var header = new byte[MatLabFile.HeaderSize];
    Encoding.ASCII.GetBytes("MATLAB 5.0 MAT-file").CopyTo(header.AsSpan(0));
    for (var i = 19; i < 124; ++i)
      header[i] = (byte)' ';

    // Version, then the two letters that say which way round the numbers are.
    header[124] = 0x00;
    header[125] = 0x01;
    header[126] = (byte)'I';
    header[127] = (byte)'M';

    output.Write(header);
    _WriteElement(output, _Matrix, array.ToArray());

    return output.ToArray();
  }

  /// <summary>Writes an element of four bytes or fewer in the short form, which is eight bytes all told.</summary>
  private static void _WriteShortElement(MemoryStream output, int type, ReadOnlySpan<byte> body) {
    Span<byte> element = stackalloc byte[8];
    BinaryPrimitives.WriteInt32LittleEndian(element, (body.Length << 16) | type);
    body[..Math.Min(body.Length, 4)].CopyTo(element[4..]);
    output.Write(element);
  }

  /// <summary>Writes one element: its type, its length, its bytes, and the padding after them.</summary>
  private static void _WriteElement(MemoryStream output, int type, ReadOnlySpan<byte> body) {
    Span<byte> tag = stackalloc byte[8];
    BinaryPrimitives.WriteInt32LittleEndian(tag, type);
    BinaryPrimitives.WriteInt32LittleEndian(tag[4..], body.Length);
    output.Write(tag);
    output.Write(body);

    for (var padding = (8 - body.Length % 8) % 8; padding > 0; --padding)
      output.WriteByte(0);
  }

  public static void ToFile(MatLabFile file, FileInfo target) {
    ArgumentNullException.ThrowIfNull(target);
    File.WriteAllBytes(target.FullName, ToBytes(file));
  }
}
