using System;
using System.Text;

namespace FileFormat.SunIcon;

/// <summary>Assembles Sun Icon file bytes from pixel data.</summary>
public static class SunIconWriter {

  private const int _VALID_BITS_PER_ITEM = 16;
  private const int _ITEMS_PER_LINE = 8;

  public static byte[] ToBytes(SunIconFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var sb = new StringBuilder();

    // Write the C comment header
    sb.Append("/* Format_version=1, ");
    sb.Append($"Width={file.Width}, ");
    sb.Append($"Height={file.Height}, ");
    sb.Append("Depth=1, ");
    sb.AppendLine($"Valid_bits_per_item={_VALID_BITS_PER_ITEM}");
    sb.AppendLine(" */");

    // Convert 1bpp pixel data to uint16 values and write as hex
    var bytesPerRow = (file.Width + 7) / 8;
    var totalPixelBytes = bytesPerRow * file.Height;

    // Pad pixel data length to even number of bytes for uint16 grouping
    var paddedLength = totalPixelBytes + (totalPixelBytes % 2);
    var padded = new byte[paddedLength];
    file.PixelData.AsSpan(0, Math.Min(totalPixelBytes, file.PixelData.Length)).CopyTo(padded.AsSpan(0));

    var itemCount = paddedLength / 2;
    for (var i = 0; i < itemCount; ++i) {
      if (i % _ITEMS_PER_LINE == 0)
        sb.Append('\t');

      var value = (ushort)((padded[i * 2] << 8) | padded[i * 2 + 1]);
      sb.Append($"0x{value:X4}");

      if (i < itemCount - 1)
        sb.Append(',');

      if (i % _ITEMS_PER_LINE == _ITEMS_PER_LINE - 1 || i == itemCount - 1)
        sb.AppendLine();
    }

    return Encoding.ASCII.GetBytes(sb.ToString());
  }
}
