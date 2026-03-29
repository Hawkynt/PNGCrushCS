using System;
using System.IO;

namespace FileFormat.Sff;

/// <summary>Assembles SFF (Structured Fax File) bytes from an SffFile data model.</summary>
public static class SffWriter {

  public static byte[] ToBytes(SffFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var pageCount = file.Pages.Count;

    // Calculate total size: file header + (page header + pixel data) per page
    var totalSize = SffHeader.StructSize;
    for (var i = 0; i < pageCount; ++i) {
      var page = file.Pages[i];
      var bytesPerRow = (page.Width + 7) / 8;
      totalSize += SffPageHeader.StructSize + bytesPerRow * page.Height;
    }

    using var ms = new MemoryStream(totalSize);
    var buffer = new byte[Math.Max(SffHeader.StructSize, SffPageHeader.StructSize)];

    // Calculate page offsets for linking
    var pageOffsets = new int[pageCount];
    var offset = SffHeader.StructSize;
    for (var i = 0; i < pageCount; ++i) {
      pageOffsets[i] = offset;
      var page = file.Pages[i];
      var bytesPerRow = (page.Width + 7) / 8;
      offset += SffPageHeader.StructSize + bytesPerRow * page.Height;
    }

    // Write file header
    var firstPageOffset = pageCount > 0 ? (ushort)pageOffsets[0] : (ushort)0;
    var fileHeader = new SffHeader(
      SffHeader.MagicByte1,
      SffHeader.MagicByte2,
      SffHeader.MagicByte3,
      SffHeader.MagicByte4,
      file.Version,
      0,
      0,
      (ushort)pageCount,
      firstPageOffset
    );
    fileHeader.WriteTo(buffer.AsSpan());
    ms.Write(buffer, 0, SffHeader.StructSize);

    // Write pages
    for (var i = 0; i < pageCount; ++i) {
      var page = file.Pages[i];
      var bytesPerRow = (page.Width + 7) / 8;
      var pixelDataLength = bytesPerRow * page.Height;
      var previousOffset = i > 0 ? (uint)pageOffsets[i - 1] : 0u;
      var nextOffset = i + 1 < pageCount ? (uint)pageOffsets[i + 1] : 0u;

      var pageHeader = new SffPageHeader(
        (ushort)pixelDataLength,
        page.VResolution,
        page.HResolution,
        0,
        0,
        (ushort)page.Width,
        (ushort)page.Height,
        previousOffset,
        nextOffset
      );
      pageHeader.WriteTo(buffer.AsSpan());
      ms.Write(buffer, 0, SffPageHeader.StructSize);

      // Write pixel data
      var writeLength = Math.Min(pixelDataLength, page.PixelData.Length);
      if (writeLength > 0)
        ms.Write(page.PixelData, 0, writeLength);

      // Pad remaining bytes if pixel data is shorter than expected
      var padding = pixelDataLength - writeLength;
      if (padding > 0) {
        var zeroPad = new byte[padding];
        ms.Write(zeroPad, 0, padding);
      }
    }

    return ms.ToArray();
  }
}
