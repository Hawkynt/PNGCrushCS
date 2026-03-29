using System;
using System.Text;

namespace FileFormat.Xwd;

/// <summary>Assembles XWD (X Window Dump) file bytes from pixel data.</summary>
public static class XwdWriter {

  private const uint _FileVersion = 7;
  private const int _ColormapEntrySize = 12;

  public static byte[] ToBytes(XwdFile file) {
    ArgumentNullException.ThrowIfNull(file);
    return Assemble(file);
  }

  internal static byte[] Assemble(XwdFile file) {
    var nameBytes = Encoding.ASCII.GetBytes(file.WindowName ?? string.Empty);
    var headerSize = (uint)(XwdHeader.StructSize + nameBytes.Length + 1); // +1 for null terminator

    var numColors = file.Colormap != null ? file.Colormap.Length / _ColormapEntrySize : 0;
    var colormapSize = numColors * _ColormapEntrySize;
    var pixelDataSize = file.BytesPerLine * file.Height;
    var totalSize = (int)headerSize + colormapSize + pixelDataSize;

    var result = new byte[totalSize];
    var span = result.AsSpan();

    var header = new XwdHeader(
      headerSize,
      _FileVersion,
      (uint)file.PixmapFormat,
      (uint)file.PixmapDepth,
      (uint)file.Width,
      (uint)file.Height,
      file.XOffset,
      file.ByteOrder,
      file.BitmapUnit,
      file.BitmapBitOrder,
      file.BitmapPad,
      (uint)file.BitsPerPixel,
      (uint)file.BytesPerLine,
      (uint)file.VisualClass,
      file.RedMask,
      file.GreenMask,
      file.BlueMask,
      file.BitsPerRgb,
      file.ColormapEntries,
      (uint)numColors,
      (uint)file.Width,
      (uint)file.Height,
      file.WindowX,
      file.WindowY,
      file.WindowBorderWidth
    );
    header.WriteTo(span);

    // Write window name + null terminator
    var nameOffset = XwdHeader.StructSize;
    nameBytes.CopyTo(result, nameOffset);
    result[nameOffset + nameBytes.Length] = 0;

    // Write colormap
    var colormapOffset = (int)headerSize;
    if (file.Colormap != null)
      file.Colormap.AsSpan(0, Math.Min(colormapSize, file.Colormap.Length)).CopyTo(result.AsSpan(colormapOffset));

    // Write pixel data
    var pixelOffset = colormapOffset + colormapSize;
    file.PixelData.AsSpan(0, Math.Min(pixelDataSize, file.PixelData.Length)).CopyTo(result.AsSpan(pixelOffset));

    return result;
  }
}
