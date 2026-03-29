using System;
using System.IO;

namespace FileFormat.FliDesigner;

/// <summary>Reads FLI Designer (.fd2) files from bytes, streams, or file paths.</summary>
public static class FliDesignerReader {

  public static FliDesignerFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("FLI Designer file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static FliDesignerFile FromStream(Stream stream) {
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

  public static FliDesignerFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < FliDesignerFile.LoadAddressSize + FliDesignerFile.MinPayloadSize)
      throw new InvalidDataException($"Data too small for a valid FLI Designer file (expected at least {FliDesignerFile.LoadAddressSize + FliDesignerFile.MinPayloadSize} bytes, got {data.Length}).");

    var loadAddress = (ushort)(data[0] | (data[1] << 8));

    var rawData = new byte[data.Length - FliDesignerFile.LoadAddressSize];
    data.AsSpan(FliDesignerFile.LoadAddressSize, rawData.Length).CopyTo(rawData.AsSpan(0));

    return new() {
      LoadAddress = loadAddress,
      RawData = rawData,
    };
  }
}
