using System;
using System.IO;

namespace FileFormat.MicroIllustrator;

/// <summary>Reads Commodore 64 Micro Illustrator files from bytes, streams, or file paths.</summary>
public static class MicroIllustratorReader {

  public static MicroIllustratorFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Micro Illustrator file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static MicroIllustratorFile FromStream(Stream stream) {
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

  public static MicroIllustratorFile FromSpan(ReadOnlySpan<byte> data) => FromBytes(data.ToArray());

  public static MicroIllustratorFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < MicroIllustratorFile.ExpectedFileSize)
      throw new InvalidDataException($"Data too small for a valid Micro Illustrator file (expected {MicroIllustratorFile.ExpectedFileSize} bytes, got {data.Length}).");

    if (data.Length != MicroIllustratorFile.ExpectedFileSize)
      throw new InvalidDataException($"Invalid Micro Illustrator file size (expected {MicroIllustratorFile.ExpectedFileSize} bytes, got {data.Length}).");

    var offset = 0;

    var loadAddress = (ushort)(data[offset] | (data[offset + 1] << 8));
    offset += MicroIllustratorFile.LoadAddressSize;

    var bitmapData = new byte[MicroIllustratorFile.BitmapDataSize];
    data.AsSpan(offset, MicroIllustratorFile.BitmapDataSize).CopyTo(bitmapData.AsSpan(0));
    offset += MicroIllustratorFile.BitmapDataSize;

    var videoMatrix = new byte[MicroIllustratorFile.VideoMatrixSize];
    data.AsSpan(offset, MicroIllustratorFile.VideoMatrixSize).CopyTo(videoMatrix.AsSpan(0));
    offset += MicroIllustratorFile.VideoMatrixSize;

    var colorRam = new byte[MicroIllustratorFile.ColorRamSize];
    data.AsSpan(offset, MicroIllustratorFile.ColorRamSize).CopyTo(colorRam.AsSpan(0));
    offset += MicroIllustratorFile.ColorRamSize;

    var backgroundColor = data[offset];

    return new() {
      LoadAddress = loadAddress,
      BitmapData = bitmapData,
      VideoMatrix = videoMatrix,
      ColorRam = colorRam,
      BackgroundColor = backgroundColor
    };
  }
}
