using System;
using System.IO;

namespace FileFormat.AtariPicworks;

/// <summary>Reads Picworks pictures from bytes, streams, or file paths.</summary>
public static class AtariPicworksReader {

  public static AtariPicworksFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Picture not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static AtariPicworksFile FromStream(Stream stream) {
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

  public static AtariPicworksFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < 4)
      throw new InvalidDataException($"Not a Picworks picture: {data.Length} bytes.");

    // The first word counts the pairs of runs; the counts occupy four bytes each.
    var countsLength = (1 + (data[0] << 8) + data[1]) << 2;
    if (data.Length <= countsLength)
      throw new InvalidDataException("A Picworks picture's counts fill the whole file.");

    var screen = new byte[AtariPicworksFile.ScreenSize];
    var value = countsLength;
    var target = 0;

    for (var count = 4; count < countsLength; count += 4) {
      var literal = ((data[count] << 8) | data[count + 1]) * AtariPicworksFile.GroupSize;
      if (value + literal + AtariPicworksFile.GroupSize > data.Length || target + literal > screen.Length)
        throw new InvalidDataException("A Picworks picture's literal run runs past the end.");

      data.Slice(value, literal).CopyTo(screen.AsSpan(target));
      value += literal;
      target += literal;

      var repeated = ((data[count + 2] << 8) | data[count + 3]) * AtariPicworksFile.GroupSize;
      if (target + repeated > screen.Length)
        throw new InvalidDataException("A Picworks picture's repeated run runs past the end.");

      // One group's worth of bytes, however many times the count says.
      for (var offset = 0; offset < repeated; offset += AtariPicworksFile.GroupSize)
        data.Slice(value, AtariPicworksFile.GroupSize).CopyTo(screen.AsSpan(target + offset));

      value += AtariPicworksFile.GroupSize;
      target += repeated;
    }

    // Whatever the runs did not account for is stored plainly, and must be exactly what is left.
    var remaining = screen.Length - target;
    if (value + remaining != data.Length)
      throw new InvalidDataException("A Picworks picture's tail does not account for the file.");

    data.Slice(value, remaining).CopyTo(screen.AsSpan(target));

    return new() { ScreenData = screen };
  }

  public static AtariPicworksFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
