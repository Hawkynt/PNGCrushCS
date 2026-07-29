using System;
using System.Buffers.Binary;
using System.Text;

namespace FileFormat.Gbr;

/// <summary>Assembles GIMP Brush (GBR) version 2 file bytes.</summary>
public static class GbrWriter {

  /// <summary>Fixed portion of the header before the name field.</summary>
  private const int _FIXED_HEADER_SIZE = 28;

  public static byte[] ToBytes(GbrFile file) {
    ArgumentNullException.ThrowIfNull(file);
    return Assemble(file.Width, file.Height, file.BytesPerPixel, file.Spacing, file.Name, file.PixelData);
  }

  internal static byte[] Assemble(int width, int height, int bytesPerPixel, int spacing, string name, byte[] pixelData) {
    var nameBytes = Encoding.UTF8.GetBytes(name ?? string.Empty);
    var headerSize = _FIXED_HEADER_SIZE + nameBytes.Length + 1; // +1 for null terminator
    var expectedPixelBytes = width * height * bytesPerPixel;
    var totalSize = headerSize + expectedPixelBytes;

    var result = new byte[totalSize];
    var span = result.AsSpan();

    BinaryPrimitives.WriteUInt32BigEndian(span, (uint)headerSize);
    new GbrHeader(2u, (uint)width, (uint)height, (uint)bytesPerPixel).WriteTo(span);

    // magic "GIMP"
    span[GbrHeader.StructSize] = (byte)'G';
    span[GbrHeader.StructSize + 1] = (byte)'I';
    span[GbrHeader.StructSize + 2] = (byte)'M';
    span[GbrHeader.StructSize + 3] = (byte)'P';

    BinaryPrimitives.WriteUInt32BigEndian(span[24..], (uint)spacing);

    // name (null-terminated)
    nameBytes.CopyTo(span[_FIXED_HEADER_SIZE..]);
    span[_FIXED_HEADER_SIZE + nameBytes.Length] = 0; // null terminator

    // pixel data
    pixelData.AsSpan(0, Math.Min(expectedPixelBytes, pixelData.Length)).CopyTo(result.AsSpan(headerSize));

    return result;
  }
}
