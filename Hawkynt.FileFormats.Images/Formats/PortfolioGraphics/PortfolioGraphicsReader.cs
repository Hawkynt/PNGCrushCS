using System;
using System.Collections.Generic;
using System.IO;

namespace FileFormat.PortfolioGraphics;

/// <summary>Reads Atari Portfolio Graphics (PGF/PGC) images from bytes, streams, or file paths.</summary>
public static class PortfolioGraphicsReader {

  public static PortfolioGraphicsFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Portfolio Graphics file not found.", file.FullName);

    var data = File.ReadAllBytes(file.FullName);
    var ext = file.Extension.ToLowerInvariant();
    return ext == ".pgc" ? _ParsePgc(data) : _ParsePgf(data);
  }

  public static PortfolioGraphicsFile FromStream(Stream stream) {
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

  public static PortfolioGraphicsFile FromSpan(ReadOnlySpan<byte> data) {
    // A full screen's worth of bytes is the screen; anything else is the run-length form.
    return data.Length == PortfolioGraphicsFile.PgfFileSize ? _ParsePgf(data) : _ParsePgc(data);
  }

  public static PortfolioGraphicsFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }

  private static PortfolioGraphicsFile _ParsePgf(ReadOnlySpan<byte> data) {
    if (data.Length < PortfolioGraphicsFile.PixelDataSize)
      throw new InvalidDataException($"PGF data too small: expected {PortfolioGraphicsFile.PixelDataSize} bytes, got {data.Length}.");

    var pixelData = data[..PortfolioGraphicsFile.PixelDataSize].ToArray();
    return new() { PixelData = pixelData };
  }

  /// <summary>
  /// Expands the run-length form: a byte with the top bit set repeats the byte after it, and one
  /// without says how many bytes follow that are taken as they stand.
  /// </summary>
  /// <remarks>
  /// This used to read the file as bytes with an escape of 0x00 introducing a count and a value, and
  /// it began at the first byte — so the three of "PG" and a version were drawn as pixels and every
  /// row after them was shifted. All three samples came back as noise where RECOIL and XnView agree
  /// on the picture.
  /// <para/>
  /// Established by rebuilding the bitmap RECOIL draws and reading the file against it: the first
  /// data byte 0xAB stands for the 43 zero bytes the picture opens with, 0x9D for the 29 that follow
  /// the first set byte, and 0x02 0x01 0xC0 for the two bytes that start the next row. All three
  /// samples now expand to exactly the 1920 bytes a screen takes.
  /// </remarks>
  private static PortfolioGraphicsFile _ParsePgc(ReadOnlySpan<byte> data) {
    if (data.Length < 2)
      throw new InvalidDataException($"PGC data too small: expected at least 2 bytes, got {data.Length}.");

    var pos = data.Length > PortfolioGraphicsFile.PgcSignature.Length
              && data[..PortfolioGraphicsFile.PgcSignature.Length].SequenceEqual(PortfolioGraphicsFile.PgcSignature)
      ? PortfolioGraphicsFile.PgcSignature.Length
      : 0;

    var pixelData = new byte[PortfolioGraphicsFile.PixelDataSize];
    var written = 0;

    while (pos < data.Length && written < PortfolioGraphicsFile.PixelDataSize) {
      var control = data[pos++];
      if (pos >= data.Length)
        break;

      if ((control & 0x80) != 0) {
        var run = Math.Min(control & 0x7F, PortfolioGraphicsFile.PixelDataSize - written);
        pixelData.AsSpan(written, run).Fill(data[pos++]);
        written += run;
        continue;
      }

      var literal = Math.Min(control, Math.Min(data.Length - pos, PortfolioGraphicsFile.PixelDataSize - written));
      data.Slice(pos, literal).CopyTo(pixelData.AsSpan(written));
      pos += control;
      written += literal;
    }

    return new() { PixelData = pixelData, Compressed = true };
  }
}
