using System;

namespace FileFormat.AtariTools800Font;

/// <summary>Assembles AtariTools-800 character set bytes.</summary>
public static class AtariTools800FontWriter {

  public static byte[] ToBytes(AtariTools800FontFile file) {
    var result = new byte[AtariTools800FontFile.FileSize];

    _Copy(file.Colors, result, 0, AtariTools800FontFile.ColorCount);
    _Copy(file.FontData, result, AtariTools800FontFile.ColorCount, AtariTools800FontFile.FontDataSize);

    return result;
  }

  private static void _Copy(byte[]? source, byte[] destination, int offset, int length) {
    var data = source ?? [];
    data.AsSpan(0, Math.Min(data.Length, length)).CopyTo(destination.AsSpan(offset));
  }
}
