using System;
using System.Buffers.Binary;

namespace FileFormat.MicroIllustrator;

/// <summary>Assembles Commodore 64 Micro Illustrator file bytes from a MicroIllustratorFile.</summary>
/// <remarks>
/// This used to write a plain Koala screen of 10003 bytes, which is not this format and which
/// nothing but the equally wrong reader beside it would read. A real file states a header length,
/// the background and the size of each of its three sections, and then gives them matrix first with
/// the bitmap last.
/// </remarks>
public static class MicroIllustratorWriter {

  public static byte[] ToBytes(MicroIllustratorFile file) {
    ArgumentNullException.ThrowIfNull(file.BitmapData);

    var result = new byte[MicroIllustratorFile.ExpectedFileSize];

    BinaryPrimitives.WriteUInt16LittleEndian(result, file.LoadAddress);
    BinaryPrimitives.WriteUInt16LittleEndian(
      result.AsSpan(MicroIllustratorFile.HeaderSizeOffset), MicroIllustratorFile.HeaderSize);
    result[MicroIllustratorFile.BackgroundOffset] = (byte)(file.BackgroundColor & 0x0F);

    // The three sizes in the order the sections themselves come in.
    var sizes = MicroIllustratorFile.SectionSizesOffset;
    BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(sizes), MicroIllustratorFile.VideoMatrixSize);
    BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(sizes + 2), MicroIllustratorFile.ColorRamSize);
    BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(sizes + 4), MicroIllustratorFile.BitmapDataSize);

    var at = MicroIllustratorFile.LoadAddressSize + MicroIllustratorFile.HeaderSize;

    file.VideoMatrix.AsSpan(0, MicroIllustratorFile.VideoMatrixSize).CopyTo(result.AsSpan(at));
    at += MicroIllustratorFile.VideoMatrixSize;

    file.ColorRam.AsSpan(0, MicroIllustratorFile.ColorRamSize).CopyTo(result.AsSpan(at));
    at += MicroIllustratorFile.ColorRamSize;

    file.BitmapData.AsSpan(0, MicroIllustratorFile.BitmapDataSize).CopyTo(result.AsSpan(at));

    return result;
  }
}
