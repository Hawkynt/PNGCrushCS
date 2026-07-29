using System;
using System.IO;

namespace FileFormat.SevenuP;

/// <summary>Reads ZX Spectrum SevenuP (.sev) images from bytes, streams, or file paths.</summary>
public static class SevenuPReader {

  public static SevenuPFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("SevenuP file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static SevenuPFile FromStream(Stream stream) {
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

  public static SevenuPFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < 23 || !data[..SevenuPFile.Signature.Length].SequenceEqual(SevenuPFile.Signature)
        || data[3] != 0 || data[6] != 1 || data[7] != 0)
      throw new InvalidDataException("Not a SevenuP file: missing the 'Sev' header.");

    var width = data[SevenuPFile.WidthOffset] | (data[SevenuPFile.WidthOffset + 1] << 8);
    var height = data[SevenuPFile.HeightOffset] | (data[SevenuPFile.HeightOffset + 1] << 8);
    if (width <= 0 || height <= 0)
      throw new InvalidDataException($"Invalid SevenuP dimensions: {width}x{height}.");

    var expected = SevenuPFile.FileSizeFor(width, height);
    if (data.Length < expected)
      throw new InvalidDataException($"SevenuP data is truncated: expected at least {expected} bytes, got {data.Length}.");

    var cells = new byte[expected - SevenuPFile.CellDataOffset];
    data.Slice(SevenuPFile.CellDataOffset, cells.Length).CopyTo(cells);

    return new() { Width = width, Height = height, CellData = cells };
  }

  public static SevenuPFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
