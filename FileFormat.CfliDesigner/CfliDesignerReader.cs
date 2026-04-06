using System;
using System.IO;

namespace FileFormat.CfliDesigner;

/// <summary>Reads CFLI Designer (.cfli) files from bytes, streams, or file paths.</summary>
public static class CfliDesignerReader {

  public static CfliDesignerFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("CFLI file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static CfliDesignerFile FromStream(Stream stream) {
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

  public static CfliDesignerFile FromSpan(ReadOnlySpan<byte> data) => FromBytes(data.ToArray());

  public static CfliDesignerFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < CfliDesignerFile.LoadAddressSize + CfliDesignerFile.MinPayloadSize)
      throw new InvalidDataException($"Data too small for a valid CFLI file (expected at least {CfliDesignerFile.LoadAddressSize + CfliDesignerFile.MinPayloadSize} bytes, got {data.Length}).");

    var loadAddress = (ushort)(data[0] | (data[1] << 8));

    var rawData = new byte[data.Length - CfliDesignerFile.LoadAddressSize];
    data.AsSpan(CfliDesignerFile.LoadAddressSize, rawData.Length).CopyTo(rawData.AsSpan(0));

    return new() {
      LoadAddress = loadAddress,
      RawData = rawData,
    };
  }
}
