using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.Ccitt;
using FileFormat.Core;

namespace FileFormat.SmartFax;

/// <summary>Reads SmartFax pages from bytes, streams, or file paths.</summary>
public static class SmartFaxReader {

  private const int _MaxDimension = 65535;

  public static SmartFaxFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("SmartFax file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static SmartFaxFile FromStream(Stream stream) {
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

  public static SmartFaxFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }

  public static SmartFaxFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < SmartFaxFile.MinFileSize)
      throw new InvalidDataException(
        $"Data too small for a SmartFax page (at least {SmartFaxFile.MinFileSize} bytes are needed, got {data.Length}).");

    if (!data[..SmartFaxFile.Signature.Length].SequenceEqual(SmartFaxFile.Signature))
      throw new InvalidDataException("Not a SmartFax page: it does not open with FAX1D.");

    var bytesPerRow = BinaryPrimitives.ReadUInt16LittleEndian(data[SmartFaxFile.BytesPerRowOffset..]);
    if (bytesPerRow < 1)
      throw new InvalidDataException("A SmartFax page states a row length of zero bytes.");

    var width = bytesPerRow * 8;
    if (width > _MaxDimension)
      throw new InvalidDataException($"A SmartFax page states a row of {width} pixels.");

    var resolution = data[SmartFaxFile.ResolutionOffset] == 0
      ? SmartFaxFile.CoarseResolution
      : SmartFaxFile.FineResolution;

    var coded = CcittFillOrder.Reverse(data[SmartFaxFile.HeaderSize..]);

    // The page states no height, so the decoder is given a ceiling and asked how far it got. The
    // ceiling is bounded by the coding's own length as well as by the converter's limit: a row costs
    // at least the twelve bits of its separator, so a short file cannot hold four thousand rows, and
    // asking for them would allocate the whole page before finding that out.
    var ceiling = Math.Min(SmartFaxFile.MaxRows, coded.Length + 1);
    var page = CcittG3Decoder.Decode(coded, width, ceiling, out var rows);
    if (rows < 1)
      throw new InvalidDataException("A SmartFax page carries no row this can decode.");

    var stride = BilevelRows.Stride(width);
    return new() {
      Width = width,
      Height = rows,
      VerticalResolution = resolution,
      PixelData = page.AsSpan(0, stride * rows).ToArray(),
    };
  }
}
