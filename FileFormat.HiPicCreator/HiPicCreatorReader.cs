using System;
using System.IO;

namespace FileFormat.HiPicCreator;

/// <summary>Reads Hi-Pic Creator (.hpc) files from bytes, streams, or file paths.</summary>
public static class HiPicCreatorReader {

  public static HiPicCreatorFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Hi-Pic Creator file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static HiPicCreatorFile FromStream(Stream stream) {
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

  public static HiPicCreatorFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < HiPicCreatorFile.LoadAddressSize + HiPicCreatorFile.MinPayloadSize)
      throw new InvalidDataException($"Data too small for a valid Hi-Pic Creator file (expected at least {HiPicCreatorFile.LoadAddressSize + HiPicCreatorFile.MinPayloadSize} bytes, got {data.Length}).");

    var loadAddress = (ushort)(data[0] | (data[1] << 8));

    var rawData = new byte[data.Length - HiPicCreatorFile.LoadAddressSize];
    data.AsSpan(HiPicCreatorFile.LoadAddressSize, rawData.Length).CopyTo(rawData.AsSpan(0));

    return new() {
      LoadAddress = loadAddress,
      RawData = rawData,
    };
  }
}
