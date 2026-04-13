using System;
using System.IO;

namespace FileFormat.FacePainter;

/// <summary>Reads Commodore 64 Face Painter files from bytes, streams, or file paths.</summary>
public static class FacePainterReader {

  public static FacePainterFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Face Painter file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static FacePainterFile FromStream(Stream stream) {
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

  public static FacePainterFile FromSpan(ReadOnlySpan<byte> data) {

    if (data.Length < FacePainterFile.ExpectedFileSize)
      throw new InvalidDataException($"Data too small for a valid Face Painter file (expected {FacePainterFile.ExpectedFileSize} bytes, got {data.Length}).");

    if (data.Length != FacePainterFile.ExpectedFileSize)
      throw new InvalidDataException($"Invalid Face Painter file size (expected {FacePainterFile.ExpectedFileSize} bytes, got {data.Length}).");

    var offset = 0;

    var loadAddress = (ushort)(data[offset] | (data[offset + 1] << 8));
    offset += FacePainterFile.LoadAddressSize;

    var bitmapData = new byte[FacePainterFile.BitmapDataSize];
    data.Slice(offset, FacePainterFile.BitmapDataSize).CopyTo(bitmapData.AsSpan(0));
    offset += FacePainterFile.BitmapDataSize;

    var videoMatrix = new byte[FacePainterFile.VideoMatrixSize];
    data.Slice(offset, FacePainterFile.VideoMatrixSize).CopyTo(videoMatrix.AsSpan(0));
    offset += FacePainterFile.VideoMatrixSize;

    var colorRam = new byte[FacePainterFile.ColorRamSize];
    data.Slice(offset, FacePainterFile.ColorRamSize).CopyTo(colorRam.AsSpan(0));

    return new() {
      LoadAddress = loadAddress,
      BitmapData = bitmapData,
      VideoMatrix = videoMatrix,
      ColorRam = colorRam,
    };
    }

  public static FacePainterFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
