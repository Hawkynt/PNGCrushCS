using System;
using System.Buffers.Binary;
using System.IO;

namespace FileFormat.PmView;

/// <summary>Reads PM pictures from bytes, streams, or file paths.</summary>
public static class PmViewReader {

  public static PmViewFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("PM picture not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static PmViewFile FromStream(Stream stream) {
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

  public static PmViewFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < PmViewFile.HeaderSize || !data[..4].SequenceEqual(PmViewFile.Magic))
      throw new InvalidDataException("Not a PM picture: it does not open with VIEW.");

    var bands = BinaryPrimitives.ReadInt32BigEndian(data[4..]);
    var rows = BinaryPrimitives.ReadInt32BigEndian(data[8..]);
    var columns = BinaryPrimitives.ReadInt32BigEndian(data[12..]);
    var form = BinaryPrimitives.ReadInt32BigEndian(data[20..]);
    var commentLength = BinaryPrimitives.ReadInt32BigEndian(data[24..]);

    if (columns <= 0 || rows <= 0)
      throw new InvalidDataException($"Invalid PM size: {columns}x{rows}.");
    if (bands is not (1 or 3))
      throw new InvalidDataException($"Invalid PM band count: {bands}. Expected 1 or 3.");
    if (form != PmViewFile.UnsignedByteForm)
      throw new InvalidDataException($"Unsupported PM storage form: 0x{form:X}. Only one byte a band is read here.");

    var plane = columns * rows;
    if (data.Length < PmViewFile.HeaderSize + plane * bands)
      throw new InvalidDataException(
        $"A {columns}x{rows} PM picture in {bands} band(s) needs {PmViewFile.HeaderSize + plane * bands} bytes, got {data.Length}.");

    // One whole band after another, not interleaved.
    var pixels = new byte[plane * bands];
    for (var band = 0; band < bands; ++band)
    for (var i = 0; i < plane; ++i)
      pixels[i * bands + band] = data[PmViewFile.HeaderSize + band * plane + i];

    // The comment trails the picture rather than sitting in the header.
    var commentAt = PmViewFile.HeaderSize + plane * bands;
    var room = Math.Max(0, Math.Min(commentLength, data.Length - commentAt));

    return new() {
      Width = columns,
      Height = rows,
      Bands = bands,
      PixelData = pixels,
      Comment = data.Slice(commentAt, room).ToArray(),
    };
  }

  public static PmViewFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
