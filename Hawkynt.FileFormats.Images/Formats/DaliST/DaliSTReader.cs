using System;
using System.Buffers.Binary;
using System.IO;

namespace FileFormat.DaliST;

/// <summary>Reads Atari ST Dali (SD0/SD1/SD2) images from bytes, streams, or file paths.</summary>
public static class DaliSTReader {

  public static DaliSTFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Dali ST file not found.", file.FullName);

    return _Parse(File.ReadAllBytes(file.FullName), DaliSTFile.ResolutionFromExtension(file.Extension));
  }

  public static DaliSTFile FromStream(Stream stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.CanSeek) {
      var data = new byte[stream.Length - stream.Position];
      stream.ReadExactly(data);
      return FromSpan(data);
    }

    using var ms = new MemoryStream();
    stream.CopyTo(ms);
    return FromSpan(ms.ToArray());
  }

  /// <summary>Reads bytes using the primary .SD0 interpretation because resolution is not stored in the file.</summary>
  public static DaliSTFile FromSpan(ReadOnlySpan<byte> data) => _Parse(data, DaliSTResolution.Low);

  public static DaliSTFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }

  public static DaliSTFile FromSpan(ReadOnlySpan<byte> data, DaliSTResolution resolution) => _Parse(data, resolution);

  public static DaliSTFile FromBytes(byte[] data, DaliSTResolution resolution) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data, resolution);
  }

  private static DaliSTFile _Parse(ReadOnlySpan<byte> data, DaliSTResolution resolution) {
    var (width, height, _) = DaliSTFile.GetMode(resolution);
    if (data.Length != DaliSTFile.ExpectedFileSize)
      throw new InvalidDataException($"A Dali ST file is exactly {DaliSTFile.ExpectedFileSize} bytes; this file is {data.Length}.");
    if (BinaryPrimitives.ReadUInt32BigEndian(data) != 0)
      throw new InvalidDataException("Dali ST file identifier must be zero.");

    var header = DaliSTHeader.ReadFrom(data[DaliSTFile.PaletteOffset..]);
    return new() {
      Width = width,
      Height = height,
      Resolution = resolution,
      Palette = header.Palette,
      ReservedData = data.Slice(DaliSTFile.ReservedOffset, DaliSTFile.ReservedSize).ToArray(),
      PixelData = data.Slice(DaliSTFile.HeaderSize, DaliSTFile.PlanarDataSize).ToArray(),
    };
  }
}
