using System;
using System.IO;
using FileFormat.Mda;

namespace FileFormat.Mdp;

/// <summary>Writes MicroDesign 3 Page (.MDP) files.</summary>
public static class MdpWriter {

  public static byte[] ToBytes(MdpFile file) {
    MdpFile.Validate(file, nameof(file));

    var result = MdaWriter.ToBytes(MdpFile.AsMda(file));
    ".MDP"u8.CopyTo(result);
    result[34] = (byte)file.Resolution;
    result[35] = (byte)file.PageFormat;
    result[36] = file.PageRamBlocks;
    return result;
  }

  public static void ToStream(MdpFile file, Stream stream) {
    ArgumentNullException.ThrowIfNull(stream);
    stream.Write(ToBytes(file));
  }

  public static void ToFile(MdpFile file, FileInfo destination) {
    ArgumentNullException.ThrowIfNull(destination);
    File.WriteAllBytes(destination.FullName, ToBytes(file));
  }
}
