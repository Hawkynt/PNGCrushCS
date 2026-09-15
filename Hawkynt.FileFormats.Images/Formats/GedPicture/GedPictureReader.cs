using System;
using System.IO;

namespace FileFormat.GedPicture;

/// <summary>Reads GED pictures from bytes, streams, or file paths.</summary>
public static class GedPictureReader {

  public static GedPictureFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Picture not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static GedPictureFile FromStream(Stream stream) => FromBytes(StreamBytes.ReadAll(stream));

  public static GedPictureFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length != GedPictureFile.FileSize
        || !data[..GedPictureFile.Signature.Length].SequenceEqual(GedPictureFile.Signature))
      throw new InvalidDataException("Not a GED picture.");

    var cycle = data[3300];
    if (cycle > 7)
      throw new InvalidDataException($"A GED picture is drawn against one of eight timings, not {cycle}.");

    return new() { Data = data.ToArray(), Cycle = cycle };
  }

  public static GedPictureFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
