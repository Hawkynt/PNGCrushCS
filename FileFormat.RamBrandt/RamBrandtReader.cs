using System;
using System.IO;

namespace FileFormat.RamBrandt;

/// <summary>Reads Ram Brandt files from bytes, streams, or file paths.</summary>
public static class RamBrandtReader {

  public static RamBrandtFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Ram Brandt file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static RamBrandtFile FromStream(Stream stream) {
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

  public static RamBrandtFile FromSpan(ReadOnlySpan<byte> data) {

    if (data.Length != RamBrandtFile.ExpectedFileSize)
      throw new InvalidDataException($"Ram Brandt file must be exactly {RamBrandtFile.ExpectedFileSize} bytes, got {data.Length}.");

    var pixelData = new byte[RamBrandtFile.ExpectedFileSize];
    data.Slice(0, RamBrandtFile.ExpectedFileSize).CopyTo(pixelData);

    return new RamBrandtFile { PixelData = pixelData };
    }

  public static RamBrandtFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length != RamBrandtFile.ExpectedFileSize)
      throw new InvalidDataException($"Ram Brandt file must be exactly {RamBrandtFile.ExpectedFileSize} bytes, got {data.Length}.");

    var pixelData = new byte[RamBrandtFile.ExpectedFileSize];
    data.AsSpan(0, RamBrandtFile.ExpectedFileSize).CopyTo(pixelData);

    return new RamBrandtFile { PixelData = pixelData };
  }
}
