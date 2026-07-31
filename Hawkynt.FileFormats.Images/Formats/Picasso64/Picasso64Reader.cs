using System;
using System.IO;

namespace FileFormat.Picasso64;

/// <summary>Reads Picasso 64 pictures from bytes, streams, or file paths.</summary>
public static class Picasso64Reader {

  public static Picasso64File FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Picture not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static Picasso64File FromStream(Stream stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.CanSeek) {
      var data = new byte[stream.Length - stream.Position];
      stream.ReadExactly(data);
      return FromSpan(data);
    }

    using var ms = new MemoryStream();
    stream.CopyTo(ms);
    return FromSpan(ms.ToArray());
  }

  public static Picasso64File FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length != Picasso64File.ExpectedFileSize)
      throw new InvalidDataException(
        $"Invalid Picasso 64 file size (expected {Picasso64File.ExpectedFileSize} bytes, got {data.Length}).");

    var bitmap = new byte[Picasso64File.BitmapDataSize];
    data.Slice(Picasso64File.BitmapOffset, Picasso64File.BitmapDataSize).CopyTo(bitmap.AsSpan(0));

    var matrix = new byte[Picasso64File.VideoMatrixSize];
    data.Slice(Picasso64File.VideoMatrixOffset, Picasso64File.VideoMatrixSize).CopyTo(matrix.AsSpan(0));

    var colors = new byte[Picasso64File.ColorRamSize];
    data.Slice(Picasso64File.ColorRamOffset, Picasso64File.ColorRamSize).CopyTo(colors.AsSpan(0));

    return new() {
      LoadAddress = (ushort)(data[0] | (data[1] << 8)),
      BitmapData = bitmap,
      VideoMatrix = matrix,
      ColorRam = colors,
      BackgroundColor = Picasso64File.BackgroundOffset < 0 ? (byte)0 : data[Picasso64File.BackgroundOffset],
    };
  }

  public static Picasso64File FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
