using System;
using System.IO;

namespace FileFormat.Astc;

/// <summary>Assembles ASTC file bytes from compressed block data.</summary>
public static class AstcWriter {

  public static byte[] ToBytes(AstcFile file) => Assemble(
    file.CompressedData,
    file.Width,
    file.Height,
    file.Depth,
    file.BlockDimX,
    file.BlockDimY,
    file.BlockDimZ
  );

  internal static byte[] Assemble(byte[] compressedData, int width, int height, int depth, byte blockDimX, byte blockDimY, byte blockDimZ) {
    var header = new AstcHeader(
      Magic: AstcHeader.MagicValue,
      BlockDimX: blockDimX,
      BlockDimY: blockDimY,
      BlockDimZ: blockDimZ,
      Width: width,
      Height: height,
      Depth: depth
    );

    var result = new byte[AstcHeader.StructSize + compressedData.Length];
    header.WriteTo(result);
    compressedData.AsSpan(0, compressedData.Length).CopyTo(result.AsSpan(AstcHeader.StructSize));

    return result;
  }
}
