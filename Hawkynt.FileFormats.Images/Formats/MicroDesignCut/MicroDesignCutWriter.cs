using System;
using System.Buffers.Binary;
using System.IO;

namespace FileFormat.MicroDesignCut;

/// <summary>Writes experimentally reconstructed MicroDesign CUT bitmaps.</summary>
public static class MicroDesignCutWriter {

  public static byte[] ToBytes(MicroDesignCutFile file) {
    MicroDesignCutFile.Validate(file, nameof(file));

    var result = new byte[checked(MicroDesignCutFile.HeaderSize + file.RasterData.Length)];
    BinaryPrimitives.WriteUInt16LittleEndian(result, file.HeightCode);
    BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(2), file.WidthCode);
    file.RasterData.CopyTo(result, MicroDesignCutFile.HeaderSize);
    return result;
  }

  public static void ToStream(MicroDesignCutFile file, Stream stream) {
    ArgumentNullException.ThrowIfNull(stream);
    stream.Write(ToBytes(file));
  }

  public static void ToFile(MicroDesignCutFile file, FileInfo destination) {
    ArgumentNullException.ThrowIfNull(destination);
    File.WriteAllBytes(destination.FullName, ToBytes(file));
  }
}
