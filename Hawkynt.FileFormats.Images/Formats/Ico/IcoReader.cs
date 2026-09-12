using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;

namespace FileFormat.Ico;

/// <summary>Reads ICO files from bytes, streams, or file paths.</summary>
public static class IcoReader {

  public static IcoFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("ICO file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static IcoFile FromStream(Stream stream) {
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

  public static IcoFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }

  public static IcoFile FromSpan(ReadOnlySpan<byte> data) => _Parse(data, IcoFileType.Icon);

  /// <summary>
  /// Reads an icon or a cursor without being told which, reporting what it turned out to be.
  /// </summary>
  /// <remarks>
  /// This is the one directory walk; <see cref="FromSpan"/> and
  /// <see cref="FileFormat.Cur.CurReader.FromSpan"/> are both this with the type checked
  /// afterwards. Reading the hotspot is not conditional on having been told to expect a cursor:
  /// the file says which it is, and the two bytes are read according to what it said.
  /// </remarks>
  /// <exception cref="ArgumentNullException"><paramref name="data"/> is null.</exception>
  /// <exception cref="InvalidDataException">
  /// The file is too short, its reserved field is not nought, it claims to be neither an icon nor a
  /// cursor, or an entry points outside it.
  /// </exception>
  public static IconBundle ReadBundle(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return ReadBundle(data.AsSpan());
  }

  /// <inheritdoc cref="ReadBundle(byte[])"/>
  public static IconBundle ReadBundle(ReadOnlySpan<byte> data) {
    if (data.Length < IcoHeader.StructSize)
      throw new InvalidDataException("Data too small for a valid ICO file.");

    var header = IcoHeader.ReadFrom(data);

    if (header.Reserved != 0)
      throw new InvalidDataException($"Invalid ICO reserved field: expected 0, got {header.Reserved}.");

    if (header.Type is not ((ushort)IcoFileType.Icon or (ushort)IcoFileType.Cursor))
      throw new InvalidDataException($"Invalid ICO type field: expected 1 (icon) or 2 (cursor), got {header.Type}.");

    var kind = (IcoFileType)header.Type;
    var isCursor = kind == IcoFileType.Cursor;

    var count = header.Count;
    var directoryEnd = IcoHeader.StructSize + count * IcoDirectoryEntry.StructSize;
    if (data.Length < directoryEnd)
      throw new InvalidDataException("Data too small to contain all directory entries.");

    var entries = new List<IconBundleEntry>(count);
    for (var i = 0; i < count; ++i) {
      var entry = IcoDirectoryEntry.ReadFrom(data[(IcoHeader.StructSize + i * IcoDirectoryEntry.StructSize)..]);
      var dataSize = entry.DataSize;
      var dataOffset = entry.DataOffset;

      if (dataSize < 0 || dataOffset < 0)
        throw new InvalidDataException($"Invalid directory entry {i}: negative size or offset.");

      if (dataOffset + dataSize > data.Length)
        throw new InvalidDataException($"Directory entry {i} references data beyond end of file.");

      var payload = data.Slice(dataOffset, dataSize).ToArray();

      // In a cursor these two bytes are the hotspot, and the depth is then only knowable from the
      // payload. Reading them as a depth is the mistake that makes a cursor whose hotspot happens
      // to be at (1, 24) look like a 24-bit picture.
      var hotspotX = isCursor ? entry.Field4 : (ushort)0;
      var hotspotY = isCursor ? entry.Field5 : (ushort)0;
      var statedBitCount = isCursor ? 0 : entry.Field5;

      // A directory entry states each side in one byte with nought standing for 256, which is not
      // what older files mean by it: one cursor in the corpus states 0 by 0 and is a 32 by 32
      // arrow, and XnView, ImageMagick and IrfanView all draw it at 32. The payload carries the
      // real size, so it is asked first and the directory byte is the fallback.
      var directoryWidth = entry.Width == 0 ? 256 : entry.Width;
      var directoryHeight = entry.Height == 0 ? 256 : entry.Height;

      if (IcoPayload.IsPng(payload)) {
        var (pngWidth, pngHeight, pngBitsPerPixel) = IcoPayload.ReadPngHeader(payload);
        entries.Add(new IconBundleEntry(
          i, pngWidth, pngHeight, pngBitsPerPixel, hotspotX, hotspotY, IcoImageFormat.Png, payload));
      } else {
        var bitsPerPixel = _ReadDibBitsPerPixel(payload, statedBitCount);
        var (dibWidth, dibHeight) = _ReadDibDimensions(payload, directoryWidth, directoryHeight);
        entries.Add(new IconBundleEntry(
          i, dibWidth, dibHeight, bitsPerPixel, hotspotX, hotspotY, IcoImageFormat.Bmp, payload));
      }
    }

    return new IconBundle(kind, entries);
  }

  internal static IcoFile _Parse(ReadOnlySpan<byte> data, IcoFileType expectedType) {
    var bundle = ReadBundle(data);

    if (bundle.Kind != expectedType)
      throw new InvalidDataException($"Invalid ICO type field: expected {(ushort)expectedType}, got {(ushort)bundle.Kind}.");

    var images = new List<IcoImage>(bundle.Entries.Count);
    foreach (var entry in bundle.Entries)
      images.Add(new IcoImage {
        Width = entry.Width,
        Height = entry.Height,
        BitsPerPixel = entry.BitsPerPixel,
        Format = entry.Format,
        Data = entry.Data
      });

    return new IcoFile { Images = images };
  }

  /// <summary>
  /// Takes the size from the bitmap header rather than from the directory entry.
  /// </summary>
  /// <remarks>
  /// The bitmap header carries the real width, and a height of twice the picture — the second half
  /// being the mask that says which pixels show through. The PNG-bodied entries already had their
  /// size read from the body this way; the bitmap-bodied ones now do too.
  /// </remarks>
  private static (int Width, int Height) _ReadDibDimensions(byte[] dibData, int directoryWidth, int directoryHeight) {
    if (dibData.Length < 12)
      return (directoryWidth, directoryHeight);

    var headerSize = BinaryPrimitives.ReadUInt32LittleEndian(dibData);
    if (headerSize < 12)
      return (directoryWidth, directoryHeight);

    var width = BinaryPrimitives.ReadInt32LittleEndian(dibData.AsSpan(4));
    var height = BinaryPrimitives.ReadInt32LittleEndian(dibData.AsSpan(8));
    if (width <= 0 || height == 0)
      return (directoryWidth, directoryHeight);

    // The stated height covers the picture and the mask below it.
    height = Math.Abs(height) / 2;

    return height > 0 ? (width, height) : (directoryWidth, directoryHeight);
  }

  private static int _ReadDibBitsPerPixel(byte[] dibData, int directoryBitCount) {
    // BMP DIB: BITMAPINFOHEADER starts at offset 0
    // Offset 14 in the DIB header = biBitCount (2 bytes LE)
    if (dibData.Length >= 16) {
      var dibBpp = BinaryPrimitives.ReadUInt16LittleEndian(dibData.AsSpan(14));
      if (dibBpp > 0)
        return dibBpp;
    }

    return directoryBitCount > 0 ? directoryBitCount : 32;
  }
}
