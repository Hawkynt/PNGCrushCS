using System;
using System.IO;

namespace FileFormat.PabloPaint;

/// <summary>Reads Atari ST Pablo Paint files from bytes, streams, or file paths.</summary>
public static class PabloPaintReader {

  public static PabloPaintFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Pablo Paint file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static PabloPaintFile FromStream(Stream stream) => FromBytes(StreamBytes.ReadAll(stream));

  public static PabloPaintFile FromSpan(ReadOnlySpan<byte> data) {

    if (data.Length < PabloPaintFile.FileSize)
      throw new InvalidDataException($"Data too small for a valid Pablo Paint file: expected at least {PabloPaintFile.FileSize} bytes, got {data.Length}.");

    if (!data[..PabloPaintFile.Banner.Length].SequenceEqual(PabloPaintFile.Banner))
      throw new InvalidDataException("Not a Pablo Paint file: missing the 'PABLO PACKED PICTURE' banner.");

    var pixelData = new byte[PabloPaintFile.PixelDataSize];
    data.Slice(PabloPaintFile.PixelDataOffset, PabloPaintFile.PixelDataSize).CopyTo(pixelData);

    return new PabloPaintFile { PixelData = pixelData };
    }

  public static PabloPaintFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
