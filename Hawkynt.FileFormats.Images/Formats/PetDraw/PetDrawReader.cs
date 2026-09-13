using System;
using System.IO;

namespace FileFormat.PetDraw;

/// <summary>Reads PetDraw64 screens from bytes, streams, or file paths.</summary>
public static class PetDrawReader {

  public static PetDrawFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("PetDraw64 screen not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static PetDrawFile FromStream(Stream stream) => FromBytes(StreamBytes.ReadAll(stream));

  public static PetDrawFile FromSpan(ReadOnlySpan<byte> data) {
    // The length is the only identification; the header carries a colour, not a signature.
    if (data.Length != PetDrawFile.FileSize)
      throw new InvalidDataException($"A PetDraw64 screen is {PetDrawFile.FileSize} bytes, got {data.Length}.");

    return new() { Data = data.ToArray() };
  }

  public static PetDrawFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
