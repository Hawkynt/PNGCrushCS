using System;
using System.IO;

namespace FileFormat.MovieMakerBackground;

/// <summary>Reads Movie Maker background (.bkg) files from bytes, streams, or file paths.</summary>
public static class MovieMakerBackgroundReader {

  public static MovieMakerBackgroundFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Movie Maker background file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static MovieMakerBackgroundFile FromStream(Stream stream) {
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

  public static MovieMakerBackgroundFile FromSpan(ReadOnlySpan<byte> data) {
    // The format carries no signature; its fixed size is the only thing identifying it.
    if (data.Length != MovieMakerBackgroundFile.FileSize)
      throw new InvalidDataException(
        $"A Movie Maker background file is exactly {MovieMakerBackgroundFile.FileSize} bytes, got {data.Length}.");

    var bitmap = new byte[MovieMakerBackgroundFile.BitmapDataSize];
    data[..MovieMakerBackgroundFile.BitmapDataSize].CopyTo(bitmap);

    var colors = new byte[MovieMakerBackgroundFile.ColorCount];
    data.Slice(MovieMakerBackgroundFile.ColorOffset, colors.Length).CopyTo(colors);

    return new() {
      BitmapData = bitmap,
      Colors = colors,
    };
  }

  public static MovieMakerBackgroundFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
