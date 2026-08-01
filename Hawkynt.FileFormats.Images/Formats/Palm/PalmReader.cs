using System;
using System.IO;

namespace FileFormat.Palm;

/// <summary>Reads Palm OS Bitmap files from bytes, streams, or file paths.</summary>
public static class PalmReader {

  public static PalmFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Palm bitmap file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static PalmFile FromStream(Stream stream) {
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

  public static PalmFile FromSpan(ReadOnlySpan<byte> data) {

    if (data.Length < PalmHeader.StructSize)
      throw new InvalidDataException("Data too small for a valid Palm bitmap file.");

    var span = data;
    var header = PalmHeader.ReadFrom(span);

    var width = (int)header.Width;
    var height = (int)header.Height;
    var bitsPerPixel = (int)header.BitsPerPixel;
    var bytesPerRow = (int)header.BytesPerRow;
    var transparentIndex = header.TransparentIndex;
    var compression = (PalmCompression)header.CompressionType;

    if (width <= 0)
      throw new InvalidDataException($"Invalid Palm bitmap width: {width}.");
    if (height <= 0)
      throw new InvalidDataException($"Invalid Palm bitmap height: {height}.");

    var offset = PalmHeader.StructSize;

    // Read the colour table, if one is really there. The flag alone does not settle it: ImageMagick
    // sets it on every Palm bitmap it writes and then writes no table at all, so a file whose header
    // and pixels already account for every byte was being turned away over a table that is not there.
    byte[]? palette = null;
    var tableEntries = _ColorTableEntries(data, offset, header.HasColorTable, bytesPerRow * height);
    if (tableEntries > 0) {
      offset += 2;
      palette = new byte[tableEntries * 3];
      for (var i = 0; i < tableEntries; ++i) {
        ++offset; // each entry names its own index, which is its position anyway
        palette[i * 3] = data[offset++];     // R
        palette[i * 3 + 1] = data[offset++]; // G
        palette[i * 3 + 2] = data[offset++]; // B
      }
    }

    // Read pixel data
    var expectedSize = bytesPerRow * height;
    byte[] pixelData;

    if (header.IsCompressed && compression == PalmCompression.Rle) {
      var remaining = data.Length - offset;
      pixelData = PalmRleCompressor.Decompress(data.Slice(offset, remaining), bytesPerRow, height);
    } else {
      if (offset + expectedSize > data.Length)
        throw new InvalidDataException($"Data too small for pixel data: expected {offset + expectedSize} bytes, got {data.Length}.");

      pixelData = new byte[expectedSize];
      data.Slice(offset, expectedSize).CopyTo(pixelData.AsSpan(0));
    }

    // Palm rounds every row up to a whole word, so a 24-pixel row at one bit a pixel occupies four
    // bytes where three would do. Nothing downstream knows about a stride, so the slack comes off
    // here — left on, each row started one byte further along than the last and the image sheared.
    pixelData = _WithoutRowPadding(pixelData, bytesPerRow, ((width * bitsPerPixel) + 7) / 8, height);

    return new PalmFile {
      Width = width,
      Height = height,
      BitsPerPixel = bitsPerPixel,
      Compression = compression,
      TransparentIndex = transparentIndex,
      PixelData = pixelData,
      Palette = palette
    };
    }

  public static PalmFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data.AsSpan());
  }

  /// <summary>Drops the word-alignment slack off the end of each row.</summary>
  private static byte[] _WithoutRowPadding(byte[] pixelData, int storedRow, int usedRow, int height) {
    if (storedRow <= usedRow)
      return pixelData;

    var result = new byte[usedRow * height];
    for (var y = 0; y < height; ++y) {
      var from = y * storedRow;
      if (from + usedRow > pixelData.Length)
        break;

      pixelData.AsSpan(from, usedRow).CopyTo(result.AsSpan(y * usedRow));
    }

    return result;
  }

  /// <summary>How many colour table entries this file actually carries, or 0 when it carries none.</summary>
  /// <remarks>
  /// A table needs its two-byte count plus four bytes an entry to fit between the header and the
  /// pixels. If what it claims would leave no room for the image, the flag is not to be believed.
  /// </remarks>
  private static int _ColorTableEntries(ReadOnlySpan<byte> data, int offset, bool flagged, int pixelBytes) {
    if (!flagged || offset + 2 > data.Length)
      return 0;

    var entries = (data[offset] << 8) | data[offset + 1];
    if (entries <= 0)
      return 0;

    return offset + 2 + (entries * 4) + pixelBytes <= data.Length ? entries : 0;
  }
}
