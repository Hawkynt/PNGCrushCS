using System;
using System.IO;

namespace FileFormat.Fbm;

/// <summary>Reads CMU Fuzzy Bitmap (FBM) files from bytes, streams, or file paths.</summary>
public static class FbmReader {

  public static FbmFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("FBM file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static FbmFile FromStream(Stream stream) {
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

  public static FbmFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < FbmHeader.StructSize)
      throw new InvalidDataException("Data too small for a valid FBM file.");

    // Validate magic
    for (var i = 0; i < FbmHeader.MagicBytes.Length; ++i)
      if (data[i] != FbmHeader.MagicBytes[i])
        throw new InvalidDataException("Invalid FBM magic signature.");

    var header = FbmHeader.ReadFrom(data.AsSpan());
    var cols = header.Cols;
    var rows = header.Rows;
    var bands = header.Bands;
    var bits = header.Bits;
    var rowLen = header.RowLen;
    var clrLen = header.ClrLen;

    if (cols <= 0)
      throw new InvalidDataException($"Invalid FBM width: {cols}.");
    if (rows <= 0)
      throw new InvalidDataException($"Invalid FBM height: {rows}.");
    if (bands is not (1 or 3))
      throw new InvalidDataException($"Invalid FBM band count: {bands}. Expected 1 or 3.");
    if (bits != 8)
      throw new InvalidDataException($"Unsupported FBM bits per band: {bits}. Only 8 is supported.");

    var dataOffset = FbmHeader.StructSize + clrLen;
    var bytesPerPixelRow = cols * bands;

    if (data.Length < dataOffset + rowLen * rows)
      throw new InvalidDataException($"Data too small for pixel data: expected {dataOffset + rowLen * rows} bytes, got {data.Length}.");

    // Strip row padding: each row in file is rowLen bytes, but actual pixel data is cols * bands bytes
    var pixelData = new byte[bytesPerPixelRow * rows];
    for (var y = 0; y < rows; ++y)
      data.AsSpan(dataOffset + y * rowLen, bytesPerPixelRow).CopyTo(pixelData.AsSpan(y * bytesPerPixelRow));

    return new FbmFile {
      Width = cols,
      Height = rows,
      Bands = bands,
      PixelData = pixelData,
      Title = header.Title ?? string.Empty,
    };
  }
}
