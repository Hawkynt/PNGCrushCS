using System;
using System.IO;

namespace FileFormat.Clp;

/// <summary>Reads CLP files from bytes, streams, or file paths.</summary>
public static class ClpReader {

  private const ushort _CF_DIB = 8;
  private const int _BITMAPINFOHEADER_SIZE = 40;

  public static ClpFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("CLP file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static ClpFile FromStream(Stream stream) {
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

  public static ClpFile FromSpan(ReadOnlySpan<byte> data) => FromBytes(data.ToArray());

  public static ClpFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < ClpHeader.StructSize)
      throw new InvalidDataException("Data too small for a valid CLP file.");

    var span = data.AsSpan();
    var header = ClpHeader.ReadFrom(span);

    if (header.FileId != ClpHeader.FileIdValue)
      throw new InvalidDataException($"Invalid CLP file ID: expected 0x{ClpHeader.FileIdValue:X4}, got 0x{header.FileId:X4}.");

    // Scan format directory to find CF_DIB
    var offset = ClpHeader.StructSize;
    for (var i = 0; i < header.FormatCount; ++i) {
      if (offset + 10 > data.Length)
        throw new InvalidDataException("Format directory truncated.");

      var formatId = BitConverter.ToUInt16(data, offset);
      var dataLength = BitConverter.ToUInt32(data, offset + 2);
      var dataOffset = BitConverter.ToUInt32(data, offset + 6);

      // Skip past fixed fields (2 + 4 + 4 = 10 bytes) then null-terminated name
      var nameStart = offset + 10;
      var nameEnd = nameStart;
      while (nameEnd < data.Length && data[nameEnd] != 0)
        ++nameEnd;

      // Skip past null terminator
      if (nameEnd < data.Length)
        ++nameEnd;

      offset = nameEnd;

      if (formatId != _CF_DIB)
        continue;

      // Parse BITMAPINFOHEADER at dataOffset
      if (dataOffset + _BITMAPINFOHEADER_SIZE > data.Length)
        throw new InvalidDataException("DIB data truncated.");

      var dibOffset = (int)dataOffset;
      var biWidth = BitConverter.ToInt32(data, dibOffset + 4);
      var biHeight = BitConverter.ToInt32(data, dibOffset + 8);
      var biBitsPerPixel = BitConverter.ToUInt16(data, dibOffset + 14);
      var biClrUsed = BitConverter.ToUInt32(data, dibOffset + 32);

      var absHeight = Math.Abs(biHeight);

      // Calculate palette size
      var paletteEntries = biBitsPerPixel <= 8
        ? (biClrUsed > 0 ? (int)biClrUsed : 1 << biBitsPerPixel)
        : 0;
      var paletteSize = paletteEntries * 4; // RGBQUAD = 4 bytes each

      byte[]? palette = null;
      if (paletteSize > 0) {
        var paletteOffset = dibOffset + _BITMAPINFOHEADER_SIZE;
        if (paletteOffset + paletteSize > data.Length)
          throw new InvalidDataException("Palette data truncated.");

        palette = new byte[paletteSize];
        data.AsSpan(paletteOffset, paletteSize).CopyTo(palette.AsSpan(0));
      }

      // Pixel data follows palette
      var pixelDataOffset = dibOffset + _BITMAPINFOHEADER_SIZE + paletteSize;
      var bytesPerRow = ((biWidth * biBitsPerPixel + 31) / 32) * 4;
      var pixelDataSize = bytesPerRow * absHeight;
      var available = (int)Math.Min(pixelDataSize, data.Length - pixelDataOffset);

      if (available <= 0)
        throw new InvalidDataException("No pixel data found.");

      var pixelData = new byte[available];
      data.AsSpan(pixelDataOffset, available).CopyTo(pixelData.AsSpan(0));

      return new ClpFile {
        Width = biWidth,
        Height = absHeight,
        BitsPerPixel = biBitsPerPixel,
        PixelData = pixelData,
        Palette = palette
      };
    }

    throw new InvalidDataException("No CF_DIB format found in CLP file.");
  }
}
