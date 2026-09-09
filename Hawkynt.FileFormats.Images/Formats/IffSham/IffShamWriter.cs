using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.Iff;
using FileFormat.Ilbm;

namespace FileFormat.IffSham;

/// <summary>Assembles IFF SHAM (Sliced HAM) bytes from an <see cref="IffShamFile"/>.</summary>
public static class IffShamWriter {

  public static byte[] ToBytes(IffShamFile file) {
    var hasPixels = file.PixelData is { Length: > 0 };
    var hasPalettes = file.ScanlinePalettes is { Length: > 0 };

    // Before the SHAM model exposed decoded state it was only a raw byte container. Preserve that
    // narrow use for source compatibility, but structured instances always go through the encoder.
    if (!hasPixels && !hasPalettes) {
      if (file.RawData is not { Length: >= IffShamFile.MinFileSize })
        throw new ArgumentException("SHAM data must contain encoded image state or legacy raw file bytes.", nameof(file));
      return file.RawData[..];
    }

    IffShamFile.ValidateForWriting(file, nameof(file));

    var planar = PlanarConverter.ChunkyToPlanar(file.PixelData, file.Width, file.Height, IffShamFile.NumPlanes);
    var shamSize = 2 + file.Height * IffShamFile.PaletteEntries * 2;
    var formDataSize = 4
      + _StoredChunkSize(BmhdChunk.StructSize)
      + _StoredChunkSize(4)
      + _StoredChunkSize(IffShamFile.PaletteBytesPerScanline)
      + _StoredChunkSize(shamSize)
      + _StoredChunkSize(planar.Length);

    using var stream = new MemoryStream(8 + formDataSize);

    _WriteChunkHeader(stream, "FORM", formDataSize);
    stream.Write("ILBM"u8);

    _WriteChunkHeader(stream, "BMHD", BmhdChunk.StructSize);
    Span<byte> bmhdBytes = stackalloc byte[BmhdChunk.StructSize];
    new BmhdChunk(
      (ushort)file.Width,
      (ushort)file.Height,
      0,
      0,
      IffShamFile.NumPlanes,
      0,
      0,
      0,
      0,
      10,
      11,
      (short)file.Width,
      (short)file.Height
    ).WriteTo(bmhdBytes);
    stream.Write(bmhdBytes);

    _WriteChunkHeader(stream, "CAMG", 4);
    _WriteUInt32BigEndian(stream, IffShamFile.HamViewportMode);

    // A CMAP is still required by ordinary ILBM readers and supplies the initial display palette.
    // SHAM then replaces it as the beam advances down the picture.
    _WriteChunkHeader(stream, "CMAP", IffShamFile.PaletteBytesPerScanline);
    stream.Write(file.ScanlinePalettes.AsSpan(0, IffShamFile.PaletteBytesPerScanline));

    _WriteChunkHeader(stream, "SHAM", shamSize);
    _WriteUInt16BigEndian(stream, 0); // version word used by every known SHAM producer
    for (var i = 0; i < file.ScanlinePalettes.Length; i += 3) {
      var red = IffShamEncoder.ToNibble(file.ScanlinePalettes[i]);
      var green = IffShamEncoder.ToNibble(file.ScanlinePalettes[i + 1]);
      var blue = IffShamEncoder.ToNibble(file.ScanlinePalettes[i + 2]);
      _WriteUInt16BigEndian(stream, (ushort)(red << 8 | green << 4 | blue));
    }

    _WriteChunkHeader(stream, "BODY", planar.Length);
    stream.Write(planar);
    if ((planar.Length & 1) != 0)
      stream.WriteByte(0);

    return stream.ToArray();
  }

  private static int _StoredChunkSize(int payloadSize) => 8 + payloadSize + (payloadSize & 1);

  private static void _WriteChunkHeader(Stream stream, string chunkId, int size) {
    Span<byte> buffer = stackalloc byte[IffChunkHeader.StructSize];
    new Riff.FourCC(chunkId).WriteTo(buffer);
    BinaryPrimitives.WriteInt32BigEndian(buffer[4..], size);
    stream.Write(buffer);
  }

  private static void _WriteUInt16BigEndian(Stream stream, ushort value) {
    Span<byte> buffer = stackalloc byte[2];
    BinaryPrimitives.WriteUInt16BigEndian(buffer, value);
    stream.Write(buffer);
  }

  private static void _WriteUInt32BigEndian(Stream stream, uint value) {
    Span<byte> buffer = stackalloc byte[4];
    BinaryPrimitives.WriteUInt32BigEndian(buffer, value);
    stream.Write(buffer);
  }
}
