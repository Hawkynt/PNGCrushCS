using System;
using System.IO;

namespace FileFormat.Xcursor;

/// <summary>Reads Xcursor files from bytes, streams, or file paths.</summary>
public static class XcursorReader {

  /// <summary>Image chunk type identifier.</summary>
  internal const uint ImageChunkType = 0xFFFD0002;

  public static XcursorFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Xcursor file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static XcursorFile FromStream(Stream stream) {
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

  public static XcursorFile FromSpan(ReadOnlySpan<byte> data) {

    if (data.Length < XcursorFileHeader.StructSize)
      throw new InvalidDataException("Data too small for a valid Xcursor file.");

    var fileHeader = XcursorFileHeader.ReadFrom(data.Slice(0, XcursorFileHeader.StructSize));

    if (fileHeader.Magic != XcursorWriter.Magic)
      throw new InvalidDataException("Invalid Xcursor magic: expected 'Xcur'.");

    var headerSize = (int)fileHeader.HeaderSize;
    var ntoc = (int)fileHeader.TocCount;

    var tocStart = headerSize;
    var tocEnd = tocStart + ntoc * XcursorTocEntry.StructSize;

    if (data.Length < tocEnd)
      throw new InvalidDataException("Data too small to contain all TOC entries.");

    for (var i = 0; i < ntoc; ++i) {
      var entryOffset = tocStart + i * XcursorTocEntry.StructSize;
      var entry = XcursorTocEntry.ReadFrom(data.Slice(entryOffset, XcursorTocEntry.StructSize));

      if (entry.Type != ImageChunkType)
        continue;

      return _ReadImageChunk(data, (int)entry.Position, (int)entry.Subtype);
    }

    throw new InvalidDataException("No image chunk found in Xcursor file.");
  }

  public static XcursorFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }

  private static XcursorFile _ReadImageChunk(ReadOnlySpan<byte> data, int position, int nominalSize) {
    if (data.Length < position + XcursorImageChunkHeader.StructSize)
      throw new InvalidDataException("Data too small for image chunk header.");

    var chunk = XcursorImageChunkHeader.ReadFrom(data.Slice(position, XcursorImageChunkHeader.StructSize));

    if (chunk.ChunkType != ImageChunkType)
      throw new InvalidDataException($"Expected image chunk type 0x{ImageChunkType:X8}, got 0x{chunk.ChunkType:X8}.");

    var pixelDataSize = (int)(chunk.Width * chunk.Height * 4);
    var pixelStart = position + XcursorImageChunkHeader.StructSize;

    if (data.Length < pixelStart + pixelDataSize)
      throw new InvalidDataException("Data too small for image pixel data.");

    var pixelData = new byte[pixelDataSize];
    data.Slice(pixelStart, pixelDataSize).CopyTo(pixelData.AsSpan(0));

    return new XcursorFile {
      Width = (int)chunk.Width,
      Height = (int)chunk.Height,
      XHot = (int)chunk.XHot,
      YHot = (int)chunk.YHot,
      NominalSize = (int)chunk.ChunkSubtype,
      Delay = (int)chunk.Delay,
      PixelData = pixelData,
    };
  }
}
