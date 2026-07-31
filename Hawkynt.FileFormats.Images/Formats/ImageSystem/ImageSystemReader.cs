using System;
using System.IO;

namespace FileFormat.ImageSystem;

/// <summary>Reads Image System pictures from bytes, streams, or file paths.</summary>
public static class ImageSystemReader {

  public static ImageSystemFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Picture not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static ImageSystemFile FromStream(Stream stream) {
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

  /// <summary>The length is the whole of the identification: the two forms have no signature.</summary>
  public static ImageSystemFile FromSpan(ReadOnlySpan<byte> data) {
    var loadAddress = data.Length >= 2 ? (ushort)(data[0] | (data[1] << 8)) : (ushort)0;

    switch (data.Length) {
      case ImageSystemFile.HiresFileSize: {
        var bitmap = new byte[ImageSystemFile.BitmapDataSize];
        data.Slice(ImageSystemFile.HiresBitmapOffset, ImageSystemFile.BitmapDataSize).CopyTo(bitmap);

        var matrix = new byte[ImageSystemFile.VideoMatrixSize];
        data.Slice(ImageSystemFile.HiresVideoMatrixOffset, ImageSystemFile.VideoMatrixSize).CopyTo(matrix);

        return new() { IsHires = true, LoadAddress = loadAddress, BitmapData = bitmap, VideoMatrix = matrix };
      }

      case ImageSystemFile.MulticolorFileSize: {
        var bitmap = new byte[ImageSystemFile.BitmapDataSize];
        data.Slice(ImageSystemFile.MulticolorBitmapOffset, ImageSystemFile.BitmapDataSize).CopyTo(bitmap);

        var matrix = new byte[ImageSystemFile.VideoMatrixSize];
        data.Slice(ImageSystemFile.MulticolorVideoMatrixOffset, ImageSystemFile.VideoMatrixSize).CopyTo(matrix);

        var colors = new byte[ImageSystemFile.ColorRamSize];
        data.Slice(ImageSystemFile.MulticolorColorRamOffset, ImageSystemFile.ColorRamSize).CopyTo(colors);

        return new() {
          IsHires = false,
          LoadAddress = loadAddress,
          BitmapData = bitmap,
          VideoMatrix = matrix,
          ColorRam = colors,
          BackgroundColor = data[ImageSystemFile.MulticolorBackgroundOffset],
        };
      }

      default:
        throw new InvalidDataException(
          $"Invalid Image System file size (expected {ImageSystemFile.HiresFileSize} or "
          + $"{ImageSystemFile.MulticolorFileSize} bytes, got {data.Length}).");
    }
  }

  public static ImageSystemFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
