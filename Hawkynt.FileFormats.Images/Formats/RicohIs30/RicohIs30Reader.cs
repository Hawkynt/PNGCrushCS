using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace FileFormat.RicohIs30;

/// <summary>Reads Ricoh IS30 scans from bytes, streams, or file paths.</summary>
public static class RicohIs30Reader {

  private const int _MaxDimension = 65535;

  public static RicohIs30File FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Ricoh IS30 file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static RicohIs30File FromStream(Stream stream) {
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

  public static RicohIs30File FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }

  public static RicohIs30File FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length <= RicohIs30File.HeaderSize)
      throw new InvalidDataException(
        $"Data too small for a Ricoh IS30 scan (more than {RicohIs30File.HeaderSize} bytes are needed, got {data.Length}).");

    if (!data[..RicohIs30File.Signature.Length].SequenceEqual(RicohIs30File.Signature))
      throw new InvalidDataException("Not a Ricoh IS30 scan: it does not open with a one and a zero.");

    if (data[RicohIs30File.MarkerOffset] != RicohIs30File.MarkerValue)
      throw new InvalidDataException("Not a Ricoh IS30 scan: the byte at offset 17 is not a two.");

    var bitsPerPixel = data[RicohIs30File.DepthSelectorOffset] == 1 ? 1 : 2;
    var resolution = _Decimal(data, RicohIs30File.ResolutionOffset, RicohIs30File.ResolutionLength, "resolution");
    var bytesPerRow = _Decimal(data, RicohIs30File.BytesPerRowOffset, RicohIs30File.BytesPerRowLength, "row length");
    var height = _Decimal(data, RicohIs30File.HeightOffset, RicohIs30File.HeightLength, "height");

    if (bytesPerRow < 1 || height < 1 || height > _MaxDimension)
      throw new InvalidDataException($"A Ricoh IS30 scan states {bytesPerRow} bytes a row and {height} rows.");

    var width = bytesPerRow * 8 / bitsPerPixel;
    if (width > _MaxDimension)
      throw new InvalidDataException($"A Ricoh IS30 scan states a row of {width} pixels.");

    var needed = bytesPerRow * height;
    var available = data.Length - RicohIs30File.HeaderSize;
    if (available < needed)
      throw new InvalidDataException(
        $"A Ricoh IS30 scan is truncated: {height} rows of {bytesPerRow} bytes need {needed}, and {available} are there.");

    return new() {
      Width = width,
      Height = height,
      BitsPerPixel = bitsPerPixel,
      Resolution = resolution,
      PixelData = data.Slice(RicohIs30File.HeaderSize, needed).ToArray(),
    };
  }

  /// <summary>Reads one of the header's ASCII decimal numbers.</summary>
  private static int _Decimal(ReadOnlySpan<byte> data, int offset, int length, string what) {
    var text = Encoding.ASCII.GetString(data.Slice(offset, length));
    if (!int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var value))
      throw new InvalidDataException($"A Ricoh IS30 scan writes its {what} as \"{text}\", which is not a decimal number.");

    return value;
  }
}
