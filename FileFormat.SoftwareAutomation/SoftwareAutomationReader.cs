using System;
using System.IO;

namespace FileFormat.SoftwareAutomation;

/// <summary>Reads Software Automation Graphics files from bytes, streams, or file paths.</summary>
public static class SoftwareAutomationReader {

  public static SoftwareAutomationFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Software Automation Graphics file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static SoftwareAutomationFile FromStream(Stream stream) {
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

  public static SoftwareAutomationFile FromSpan(ReadOnlySpan<byte> data) => FromBytes(data.ToArray());

  public static SoftwareAutomationFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length != SoftwareAutomationFile.ExpectedFileSize)
      throw new InvalidDataException($"Invalid Software Automation Graphics data size: expected exactly {SoftwareAutomationFile.ExpectedFileSize} bytes, got {data.Length}.");

    var pixelData = new byte[SoftwareAutomationFile.ExpectedFileSize];
    data.AsSpan(0, SoftwareAutomationFile.ExpectedFileSize).CopyTo(pixelData);

    return new SoftwareAutomationFile { PixelData = pixelData };
  }
}
