using System;
using System.Buffers.Binary;
using System.IO;

namespace FileFormat.MicroDesignGrf;

/// <summary>Writes the experimental MicroDesign GRF bitmap layout.</summary>
public static class MicroDesignGrfWriter {

  public static byte[] ToBytes(MicroDesignGrfFile file) {
    MicroDesignGrfFile.Validate(file, nameof(file));

    var result = new byte[checked(MicroDesignGrfFile.HeaderSize + file.RasterData.Length)];
    BinaryPrimitives.WriteUInt16LittleEndian(result, checked((ushort)file.Width));
    BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(2), checked((ushort)file.Height));
    file.RasterData.CopyTo(result, MicroDesignGrfFile.HeaderSize);
    return result;
  }

  public static void ToStream(MicroDesignGrfFile file, Stream stream) {
    ArgumentNullException.ThrowIfNull(stream);
    stream.Write(ToBytes(file));
  }

  public static void ToFile(MicroDesignGrfFile file, FileInfo destination) {
    ArgumentNullException.ThrowIfNull(destination);
    File.WriteAllBytes(destination.FullName, ToBytes(file));
  }
}
