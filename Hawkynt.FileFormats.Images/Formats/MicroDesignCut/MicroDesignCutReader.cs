using System;
using System.Buffers.Binary;
using System.IO;

namespace FileFormat.MicroDesignCut;

/// <summary>Reads experimentally reconstructed MicroDesign CUT bitmaps.</summary>
public static class MicroDesignCutReader {

  public static MicroDesignCutFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("MicroDesign CUT file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static MicroDesignCutFile FromStream(Stream stream) {
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

  public static MicroDesignCutFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }

  public static MicroDesignCutFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < MicroDesignCutFile.HeaderSize)
      throw new InvalidDataException("Truncated MicroDesign CUT header.");

    var heightCode = BinaryPrimitives.ReadUInt16LittleEndian(data);
    var widthCode = BinaryPrimitives.ReadUInt16LittleEndian(data[2..]);
    var width = MicroDesignCutFile.GetWidth(widthCode);
    var height = MicroDesignCutFile.GetHeight(heightCode);

    try {
      MicroDesignCutFile.ValidateDimensions(width, height, nameof(data));
    } catch (ArgumentOutOfRangeException exception) {
      throw new InvalidDataException(exception.Message, exception);
    }

    var rasterLength = checked(MicroDesignCutFile.GetRowStride(width) * height);
    var expectedLength = checked(MicroDesignCutFile.HeaderSize + rasterLength);
    if (data.Length < expectedLength)
      throw new InvalidDataException($"Truncated MicroDesign CUT raster: expected {expectedLength} bytes, found {data.Length}.");
    if (data.Length > expectedLength)
      throw new InvalidDataException($"Unexpected trailing MicroDesign CUT data: expected {expectedLength} bytes, found {data.Length}.");

    return new() {
      HeightCode = heightCode,
      WidthCode = widthCode,
      RasterData = data[MicroDesignCutFile.HeaderSize..].ToArray(),
    };
  }
}
