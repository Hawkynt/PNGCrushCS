using System;
using System.Buffers.Binary;
using System.IO;

namespace FileFormat.MicroDesignGrf;

/// <summary>Reads the experimental MicroDesign GRF bitmap layout.</summary>
public static class MicroDesignGrfReader {

  public static MicroDesignGrfFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("MicroDesign GRF file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static MicroDesignGrfFile FromStream(Stream stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.CanSeek) {
      var remaining = checked((int)(stream.Length - stream.Position));
      var data = new byte[remaining];
      stream.ReadExactly(data);
      return FromBytes(data);
    }

    using var buffer = new MemoryStream();
    stream.CopyTo(buffer);
    return FromBytes(buffer.ToArray());
  }

  public static MicroDesignGrfFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }

  public static MicroDesignGrfFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < MicroDesignGrfFile.HeaderSize)
      throw new InvalidDataException("Truncated MicroDesign GRF header.");

    var width = BinaryPrimitives.ReadUInt16LittleEndian(data);
    var height = BinaryPrimitives.ReadUInt16LittleEndian(data[2..]);
    try {
      MicroDesignGrfFile.ValidateDimensions(width, height, nameof(data));
    } catch (ArgumentOutOfRangeException exception) {
      throw new InvalidDataException(exception.Message, exception);
    }

    var rasterLength = checked(MicroDesignGrfFile.GetRowStride(width) * height);
    var expectedLength = checked(MicroDesignGrfFile.HeaderSize + rasterLength);
    if (data.Length < expectedLength)
      throw new InvalidDataException("Truncated MicroDesign GRF raster.");
    if (data.Length > expectedLength)
      throw new InvalidDataException("Unexpected trailing MicroDesign GRF data.");

    return new() {
      Width = width,
      Height = height,
      RasterData = data[MicroDesignGrfFile.HeaderSize..].ToArray(),
    };
  }
}
