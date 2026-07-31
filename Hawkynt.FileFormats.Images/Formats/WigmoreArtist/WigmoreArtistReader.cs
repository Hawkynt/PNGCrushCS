using System;
using System.IO;

namespace FileFormat.WigmoreArtist;

/// <summary>Reads Wigmore Artist pictures from bytes, streams, or file paths.</summary>
public static class WigmoreArtistReader {

  public static WigmoreArtistFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Picture not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static WigmoreArtistFile FromStream(Stream stream) {
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

  public static WigmoreArtistFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length != WigmoreArtistFile.ExpectedFileSize)
      throw new InvalidDataException(
        $"Invalid Wigmore Artist file size (expected {WigmoreArtistFile.ExpectedFileSize} bytes, got {data.Length}).");

    var bitmap = new byte[WigmoreArtistFile.BitmapDataSize];
    data.Slice(WigmoreArtistFile.BitmapOffset, WigmoreArtistFile.BitmapDataSize).CopyTo(bitmap.AsSpan(0));

    var matrix = new byte[WigmoreArtistFile.VideoMatrixSize];
    data.Slice(WigmoreArtistFile.VideoMatrixOffset, WigmoreArtistFile.VideoMatrixSize).CopyTo(matrix.AsSpan(0));

    var colors = new byte[WigmoreArtistFile.ColorRamSize];
    data.Slice(WigmoreArtistFile.ColorRamOffset, WigmoreArtistFile.ColorRamSize).CopyTo(colors.AsSpan(0));

    return new() {
      LoadAddress = (ushort)(data[0] | (data[1] << 8)),
      BitmapData = bitmap,
      VideoMatrix = matrix,
      ColorRam = colors,
      BackgroundColor = WigmoreArtistFile.BackgroundOffset < 0 ? (byte)0 : data[WigmoreArtistFile.BackgroundOffset],
    };
  }

  public static WigmoreArtistFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
