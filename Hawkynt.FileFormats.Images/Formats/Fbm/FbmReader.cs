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

  public static FbmFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < FbmHeader.StructSize)
      throw new InvalidDataException("Data too small for a valid FBM file.");

    for (var i = 0; i < FbmHeader.MagicBytes.Length; ++i)
      if (data[i] != FbmHeader.MagicBytes[i])
        throw new InvalidDataException("Invalid FBM magic signature.");

    var header = FbmHeader.ReadFrom(data);
    var (cols, rows, bands) = (header.Cols, header.Rows, header.Bands);

    if (cols <= 0)
      throw new InvalidDataException($"Invalid FBM width: {cols}.");
    if (rows <= 0)
      throw new InvalidDataException($"Invalid FBM height: {rows}.");
    if (bands is not (1 or 3))
      throw new InvalidDataException($"Invalid FBM band count: {bands}. Expected 1 or 3.");
    if (header.Bits != 8)
      throw new InvalidDataException($"Unsupported FBM bits per band: {header.Bits}. Only 8 is supported.");

    // One row of one plane, not of all of them: the bands are stored one whole plane after another
    // rather than interleaved, which is what reading rowlen as the interleaved stride got wrong.
    var rowLen = header.RowLen > 0 ? header.RowLen : cols;
    var planeLen = header.PlnLen > 0 ? header.PlnLen : rowLen * rows;
    var dataOffset = FbmHeader.StructSize + header.ClrLen;

    if (data.Length < dataOffset + planeLen * bands)
      throw new InvalidDataException(
        $"Data too small for pixel data: expected {dataOffset + planeLen * bands} bytes, got {data.Length}.");

    // Bottom to top, so the last stored row is the top of the picture.
    var pixels = new byte[cols * rows * bands];
    for (var band = 0; band < bands; ++band)
    for (var y = 0; y < rows; ++y) {
      var source = dataOffset + band * planeLen + (rows - 1 - y) * rowLen;
      for (var x = 0; x < cols; ++x)
        pixels[(y * cols + x) * bands + band] = data[source + x];
    }

    return new FbmFile {
      Width = cols,
      Height = rows,
      Bands = bands,
      PixelData = pixels,
      Title = header.Title ?? string.Empty,
    };
  }

  public static FbmFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
