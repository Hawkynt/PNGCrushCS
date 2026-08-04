using System;
using System.Buffers.Binary;
using System.IO;

namespace FileFormat.MicroIllustrator;

/// <summary>Reads Commodore 64 Micro Illustrator files from bytes, streams, or file paths.</summary>
/// <remarks>
/// This used to want 10003 bytes laid out as bitmap, matrix, colour and a background byte — a plain
/// Koala screen under a different name. No Micro Illustrator file is that: they are 10022 bytes, they
/// carry a twenty-byte header stating where everything is, and their sections come in the opposite
/// order, the bitmap last. Both samples were refused outright, and RECOIL accepts nothing but 10022.
/// </remarks>
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

  public static MicroIllustratorFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length != MicroIllustratorFile.ExpectedFileSize)
      throw new InvalidDataException($"A Micro Illustrator file is {MicroIllustratorFile.ExpectedFileSize} bytes; this one is {data.Length}.");

    // The picture is the last ten thousand bytes, whatever the header says about its own length.
    // One of the two samples states twenty there and the other states nought, and both are drawn
    // from the same place — so the field is a description of the header rather than a pointer, and
    // trusting it refuses half the files that exist.
    //
    // Matrix, then colour, then the bitmap: the order the header lists their sizes in at bytes nine
    // to fourteen, and the opposite of every other C64 picture here.
    var at = MicroIllustratorFile.PictureOffset;

    var videoMatrix = data.Slice(at, MicroIllustratorFile.VideoMatrixSize).ToArray();
    at += MicroIllustratorFile.VideoMatrixSize;

    var colorRam = data.Slice(at, MicroIllustratorFile.ColorRamSize).ToArray();
    at += MicroIllustratorFile.ColorRamSize;

    var bitmapData = data.Slice(at, MicroIllustratorFile.BitmapDataSize).ToArray();

    return new() {
      LoadAddress = BinaryPrimitives.ReadUInt16LittleEndian(data),
      BitmapData = bitmapData,
      VideoMatrix = videoMatrix,
      ColorRam = colorRam,
      BackgroundColor = (byte)(data[MicroIllustratorFile.BackgroundOffset] & 0x0F),
    };
  }

  public static MicroIllustratorFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
