using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.Ccitt;

namespace FileFormat.MicroDynamicsMars;

/// <summary>Reads Micro Dynamics MARS pages from bytes, streams, or file paths.</summary>
public static class MicroDynamicsMarsReader {

  /// <summary>The largest page this will build, which keeps a corrupt header from asking for a gigabyte.</summary>
  private const int _MaxDimension = 65535;

  public static MicroDynamicsMarsFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Micro Dynamics MARS file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static MicroDynamicsMarsFile FromStream(Stream stream) {
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

  public static MicroDynamicsMarsFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }

  public static MicroDynamicsMarsFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length <= MicroDynamicsMarsFile.HeaderSize)
      throw new InvalidDataException(
        $"Data too small for a Micro Dynamics MARS page (more than {MicroDynamicsMarsFile.HeaderSize} bytes are needed, got {data.Length}).");

    if (!data[..MicroDynamicsMarsFile.Signature.Length].SequenceEqual(MicroDynamicsMarsFile.Signature))
      throw new InvalidDataException("Not a Micro Dynamics MARS page: it does not open with a two and PBIT.");

    var resolution = BinaryPrimitives.ReadInt32BigEndian(data[MicroDynamicsMarsFile.ResolutionOffset..]);
    var height = BinaryPrimitives.ReadInt32BigEndian(data[MicroDynamicsMarsFile.HeightOffset..]);
    var width = BinaryPrimitives.ReadInt32BigEndian(data[MicroDynamicsMarsFile.WidthOffset..]);

    if (width < 1 || height < 1 || width > _MaxDimension || height > _MaxDimension)
      throw new InvalidDataException($"A Micro Dynamics MARS page states a picture of {width}x{height}.");

    var coded = data[MicroDynamicsMarsFile.HeaderSize..].ToArray();
    var pixelData = CcittG4Decoder.Decode(coded, width, height, out var rowsDecoded);
    if (rowsDecoded != height)
      throw new InvalidDataException(
        $"A Micro Dynamics MARS page's Group 4 coding runs out after {rowsDecoded} of the {height} rows its header states.");

    return new() { Width = width, Height = height, Resolution = resolution, PixelData = pixelData };
  }
}
