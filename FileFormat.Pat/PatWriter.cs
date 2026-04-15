using System;
using System.Text;

namespace FileFormat.Pat;

/// <summary>Assembles GIMP Pattern (PAT) file bytes.</summary>
public static class PatWriter {

  /// <summary>Fixed header fields before the name: header_size(4) + version(4) + width(4) + height(4) + bpp(4) + magic(4) = 24 bytes.</summary>
  private const int _FIXED_HEADER_SIZE = 24;

  public static byte[] ToBytes(PatFile file) {
    ArgumentNullException.ThrowIfNull(file);
    return Assemble(file.Width, file.Height, file.BytesPerPixel, file.Name, file.PixelData);
  }

  internal static byte[] Assemble(int width, int height, int bytesPerPixel, string name, byte[] pixelData) {
    var nameBytes = Encoding.UTF8.GetBytes(name ?? string.Empty);
    var headerSize = _FIXED_HEADER_SIZE + nameBytes.Length + 1; // +1 for null terminator
    var expectedPixelBytes = width * height * bytesPerPixel;
    var result = new byte[headerSize + expectedPixelBytes];
    var span = result.AsSpan();

    new PatHeader((uint)headerSize, 1u, (uint)width, (uint)height, (uint)bytesPerPixel).WriteTo(span);

    // Magic "GPAT" at offset 20
    result[PatHeader.StructSize] = (byte)'G';
    result[PatHeader.StructSize + 1] = (byte)'P';
    result[PatHeader.StructSize + 2] = (byte)'A';
    result[PatHeader.StructSize + 3] = (byte)'T';

    // Name (UTF-8, null-terminated)
    nameBytes.AsSpan(0, nameBytes.Length).CopyTo(result.AsSpan(_FIXED_HEADER_SIZE));
    result[_FIXED_HEADER_SIZE + nameBytes.Length] = 0; // null terminator

    // Pixel data
    pixelData.AsSpan(0, Math.Min(expectedPixelBytes, pixelData.Length)).CopyTo(result.AsSpan(headerSize));

    return result;
  }
}
