using System;
using System.IO;
using System.Text;

namespace FileFormat.Xwd;

/// <summary>Reads XWD (X Window Dump) files from bytes, streams, or file paths.</summary>
public static class XwdReader {

  private const int _ColormapEntrySize = 12;

  public static XwdFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("XWD file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static XwdFile FromStream(Stream stream) {
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

  public static XwdFile FromSpan(ReadOnlySpan<byte> data) => FromBytes(data.ToArray());

  public static XwdFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < XwdHeader.StructSize)
      throw new InvalidDataException("Data too small for a valid XWD file.");

    var span = data.AsSpan();
    var header = XwdHeader.ReadFrom(span);

    if (header.FileVersion != 7)
      throw new InvalidDataException($"Invalid XWD file version: {header.FileVersion}, expected 7.");

    if (header.HeaderSize < XwdHeader.StructSize)
      throw new InvalidDataException($"Invalid XWD header size: {header.HeaderSize}.");

    var width = (int)header.PixmapWidth;
    var height = (int)header.PixmapHeight;

    if (width <= 0)
      throw new InvalidDataException($"Invalid XWD width: {width}.");
    if (height <= 0)
      throw new InvalidDataException($"Invalid XWD height: {height}.");

    // Read null-terminated window name from bytes[100..HeaderSize]
    var windowName = string.Empty;
    var nameLength = (int)header.HeaderSize - XwdHeader.StructSize;
    if (nameLength > 0) {
      var nameSpan = span.Slice(XwdHeader.StructSize, nameLength);
      var nullIndex = nameSpan.IndexOf((byte)0);
      windowName = nullIndex <= 0
        ? nullIndex == 0
          ? string.Empty
          : Encoding.ASCII.GetString(nameSpan)
        : Encoding.ASCII.GetString(nameSpan[..nullIndex]);
    }

    var offset = (int)header.HeaderSize;
    var numColors = (int)header.NumColors;

    // Read colormap entries
    byte[]? colormap = null;
    if (numColors > 0) {
      var colormapSize = numColors * _ColormapEntrySize;
      if (data.Length < offset + colormapSize)
        throw new InvalidDataException("Data too small for colormap entries.");

      colormap = new byte[colormapSize];
      data.AsSpan(offset, colormapSize).CopyTo(colormap.AsSpan(0));
      offset += colormapSize;
    }

    // Read pixel data
    var bytesPerLine = (int)header.BytesPerLine;
    var pixelDataSize = bytesPerLine * height;

    if (data.Length < offset + pixelDataSize)
      throw new InvalidDataException($"Data too small for pixel data: expected {offset + pixelDataSize} bytes, got {data.Length}.");

    var pixelData = new byte[pixelDataSize];
    data.AsSpan(offset, pixelDataSize).CopyTo(pixelData.AsSpan(0));

    return new XwdFile {
      Width = width,
      Height = height,
      BitsPerPixel = (int)header.BitsPerPixel,
      BytesPerLine = bytesPerLine,
      PixmapFormat = (XwdPixmapFormat)header.PixmapFormat,
      PixmapDepth = (int)header.PixmapDepth,
      VisualClass = (XwdVisualClass)header.VisualClass,
      ByteOrder = header.ByteOrder,
      BitmapUnit = header.BitmapUnit,
      BitmapBitOrder = header.BitmapBitOrder,
      BitmapPad = header.BitmapPad,
      XOffset = header.XOffset,
      BitsPerRgb = header.BitsPerRgb,
      ColormapEntries = header.ColormapEntries,
      RedMask = header.RedMask,
      GreenMask = header.GreenMask,
      BlueMask = header.BlueMask,
      WindowX = header.WindowX,
      WindowY = header.WindowY,
      WindowBorderWidth = header.WindowBorderWidth,
      WindowName = windowName,
      PixelData = pixelData,
      Colormap = colormap
    };
  }
}
