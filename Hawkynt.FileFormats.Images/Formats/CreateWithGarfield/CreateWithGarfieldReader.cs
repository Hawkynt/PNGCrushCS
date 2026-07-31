using System;
using System.IO;

namespace FileFormat.CreateWithGarfield;

/// <summary>Reads Commodore 64 Create with Garfield hires files from bytes, streams, or file paths.</summary>
public static class CreateWithGarfieldReader {

  public static CreateWithGarfieldFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Create with Garfield file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static CreateWithGarfieldFile FromStream(Stream stream) {
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

  public static CreateWithGarfieldFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length != CreateWithGarfieldFile.ExpectedFileSize)
      throw new InvalidDataException(
        $"Invalid Create with Garfield file size (expected {{CreateWithGarfieldFile.ExpectedFileSize}} bytes, got {{data.Length}}).");

    var loadAddress = (ushort)(data[0] | (data[1] << 8));

    var bitmapData = new byte[CreateWithGarfieldFile.BitmapDataSize];
    data.Slice(CreateWithGarfieldFile.BitmapOffset, CreateWithGarfieldFile.BitmapDataSize).CopyTo(bitmapData.AsSpan(0));

    var videoMatrix = new byte[CreateWithGarfieldFile.VideoMatrixSize];
    data.Slice(CreateWithGarfieldFile.VideoMatrixOffset, CreateWithGarfieldFile.VideoMatrixSize).CopyTo(videoMatrix.AsSpan(0));

    var colorRam = new byte[CreateWithGarfieldFile.ColorRamSize];
    data.Slice(CreateWithGarfieldFile.ColorRamOffset, CreateWithGarfieldFile.ColorRamSize).CopyTo(colorRam.AsSpan(0));

    return new() {
      LoadAddress = loadAddress,
      BitmapData = bitmapData,
      VideoMatrix = videoMatrix,
      ColorRam = colorRam,
      BackgroundColor = data[CreateWithGarfieldFile.BackgroundOffset],
    };
  }

  public static CreateWithGarfieldFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
