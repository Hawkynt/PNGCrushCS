using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using FileFormat.EmbeddedDib;
using FileFormat.Riff;

namespace FileFormat.Cdr;

/// <summary>Reads direct RIFF-based CorelDRAW files (CorelDRAW X3 and older).</summary>
public static class CdrReader {

  public static CdrFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("CDR file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static CdrFile FromStream(Stream stream) {
    ArgumentNullException.ThrowIfNull(stream);

    if (stream.CanSeek) {
      var data = new byte[stream.Length - stream.Position];
      stream.ReadExactly(data);
      return FromSpan(data);
    }

    using var buffer = new MemoryStream();
    stream.CopyTo(buffer);
    return FromSpan(buffer.ToArray());
  }

  public static CdrFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }

  public static CdrFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < RiffHeader.StructSize)
      throw new InvalidDataException("Only direct RIFF-based CDR files are supported; the input is too short for a RIFF header.");

    var header = RiffHeader.ReadFrom(data);
    if (header.ChunkId.ToString() != "RIFF" || !CdrFile.IsDirectRiffCdrForm(header.FormType))
      throw new InvalidDataException("Only direct RIFF-based CorelDRAW CDR files (through X3) are supported.");

    var declaredLength = (long)header.Size + 8;
    if (declaredLength != data.Length)
      throw new InvalidDataException(
        declaredLength > data.Length
          ? $"The CDR RIFF header declares {declaredLength} bytes, but only {data.Length} are available."
          : $"The CDR RIFF header ends at byte {declaredLength}, but the file contains {data.Length} bytes.");

    var chunks = new List<RiffChunk>();
    ushort? version = null;
    FileFormat.Core.RawImage? preview = null;

    var offset = RiffHeader.StructSize;
    while (offset < data.Length) {
      if (data.Length - offset < RiffChunkHeader.StructSize)
        throw new InvalidDataException($"The CDR file ends inside a RIFF chunk header at byte {offset}.");

      var chunkHeader = RiffChunkHeader.ReadFrom(data[offset..]);
      var bodyStart = offset + RiffChunkHeader.StructSize;
      var bodyEnd64 = (long)bodyStart + chunkHeader.Size;
      if (bodyEnd64 > data.Length)
        throw new InvalidDataException(
          $"CDR chunk {chunkHeader.ChunkId} at byte {offset} declares {chunkHeader.Size} bytes beyond the end of the file.");

      var bodyEnd = checked((int)bodyEnd64);
      var body = data[bodyStart..bodyEnd].ToArray();
      chunks.Add(new RiffChunk { Id = chunkHeader.ChunkId, Data = body });

      switch (chunkHeader.ChunkId.ToString()) {
        case "vrsn":
          if (body.Length != sizeof(ushort))
            throw new InvalidDataException($"The CDR vrsn chunk must contain exactly two bytes, not {body.Length}.");
          version ??= BinaryPrimitives.ReadUInt16LittleEndian(body);
          break;

        case "DISP" when preview is null:
          if (body.Length < sizeof(uint) + 40)
            throw new InvalidDataException("The CDR DISP chunk is too short for its four-byte prefix and BITMAPINFOHEADER.");

          try {
            preview = EmbeddedDibReader.DecodeHeaderless(body.AsSpan(sizeof(uint)));
          } catch (Exception ex) when (ex is InvalidDataException or ArgumentException or NotSupportedException) {
            throw new InvalidDataException("The CDR DISP chunk does not contain a valid packed DIB preview.", ex);
          }
          break;
      }

      var paddedEnd = bodyEnd64 + (chunkHeader.Size & 1u);
      if (paddedEnd > data.Length)
        throw new InvalidDataException($"CDR chunk {chunkHeader.ChunkId} is missing its RIFF word-alignment padding byte.");

      offset = checked((int)paddedEnd);
    }

    return new CdrFile {
      FormType = header.FormType,
      Chunks = chunks,
      Version = version,
      Preview = preview,
    };
  }

}
