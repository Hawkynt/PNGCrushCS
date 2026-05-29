using System;

namespace FileFormat.Xcursor;

/// <summary>Assembles Xcursor file bytes from an <see cref="XcursorFile"/>.</summary>
public static class XcursorWriter {

  /// <summary>Current file format version.</summary>
  internal const uint FileVersion = 0x00010000;

  /// <summary>The "Xcur" magic as a LE uint32.</summary>
  internal const uint Magic = 0x72756358;

  public static byte[] ToBytes(XcursorFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var pixelDataSize = file.Width * file.Height * 4;

    var tocStart = XcursorFileHeader.StructSize;
    var imageChunkStart = tocStart + XcursorTocEntry.StructSize;
    var totalSize = imageChunkStart + XcursorImageChunkHeader.StructSize + pixelDataSize;

    var result = new byte[totalSize];
    var span = result.AsSpan();

    var fileHeader = new XcursorFileHeader(Magic, XcursorFileHeader.StructSize, FileVersion, 1);
    fileHeader.WriteTo(span);

    var tocEntry = new XcursorTocEntry(XcursorReader.ImageChunkType, (uint)file.NominalSize, (uint)imageChunkStart);
    tocEntry.WriteTo(span[tocStart..]);

    var chunkHeader = new XcursorImageChunkHeader(
      XcursorImageChunkHeader.StructSize,
      XcursorReader.ImageChunkType,
      (uint)file.NominalSize,
      1,
      (uint)file.Width,
      (uint)file.Height,
      (uint)file.XHot,
      (uint)file.YHot,
      (uint)file.Delay
    );
    chunkHeader.WriteTo(span[imageChunkStart..]);

    var copyLen = Math.Min(pixelDataSize, file.PixelData.Length);
    file.PixelData.AsSpan(0, copyLen).CopyTo(result.AsSpan(imageChunkStart + XcursorImageChunkHeader.StructSize));

    return result;
  }
}
