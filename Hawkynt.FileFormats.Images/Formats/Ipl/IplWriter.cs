using System;
using System.Buffers.Binary;
using System.Text;

namespace FileFormat.Ipl;

/// <summary>Assembles IPL Image Sequence frame file bytes.</summary>
/// <remarks>
/// Writes what <see cref="IplReader"/> reads: the 44-byte header, one 8-bit plane per channel, and
/// the "fini" trailer. What stood here before emitted a 16-byte header holding nothing but a 16-bit
/// width and height, which no other reader of this format would recognise — the round trip passed
/// only because the reader was wrong in the same way.
/// </remarks>
public static class IplWriter {

  private const int _Channels = 3;

  public static byte[] ToBytes(IplFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var width = file.Width;
    var height = file.Height;
    var planeLength = width * height;
    var result = new byte[IplReader.HeaderSize + (planeLength * _Channels) + 8];

    Encoding.ASCII.GetBytes("iiii").CopyTo(result.AsSpan(0));
    BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(4), 4);
    Encoding.ASCII.GetBytes("100f").CopyTo(result.AsSpan(8));
    Encoding.ASCII.GetBytes("data").CopyTo(result.AsSpan(12));
    BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(16), (planeLength * _Channels) + 28);
    BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(20), width);
    BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(24), height);
    BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(28), _Channels);
    BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(32), 1); // z
    BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(36), 1); // time
    BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(40), 0); // 8-bit unsigned samples

    // Interleaved RGB in, one plane a channel out.
    var pixels = file.PixelData;
    for (var i = 0; i < planeLength; ++i) {
      var source = i * 3;
      if (source + 2 >= pixels.Length)
        break;

      result[IplReader.HeaderSize + i] = pixels[source];
      result[IplReader.HeaderSize + planeLength + i] = pixels[source + 1];
      result[IplReader.HeaderSize + (planeLength * 2) + i] = pixels[source + 2];
    }

    Encoding.ASCII.GetBytes("fini").CopyTo(result.AsSpan(IplReader.HeaderSize + (planeLength * _Channels)));
    return result;
  }
}
