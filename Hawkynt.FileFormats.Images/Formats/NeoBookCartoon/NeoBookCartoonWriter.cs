using System;
using System.Buffers.Binary;
using System.IO;

namespace FileFormat.NeoBookCartoon;

/// <summary>Assembles NeoBook cartoons (.car) around their PNG payload.</summary>
public static class NeoBookCartoonWriter {

  public static byte[] ToBytes(NeoBookCartoonFile file) {
    var picture = file.Picture ?? throw new InvalidDataException("A NeoBook cartoon carries no PNG.");
    var offset = file.PictureOffset == 0 ? NeoBookCartoonFile.DefaultPictureOffset : file.PictureOffset;
    if (offset < NeoBookCartoonFile.HeaderSize)
      throw new InvalidDataException($"The picture offset must be at least {NeoBookCartoonFile.HeaderSize} bytes.");

    int length;
    try {
      length = checked(offset + picture.Length);
    } catch (OverflowException exception) {
      throw new InvalidDataException("The NeoBook cartoon is too large to serialize.", exception);
    }

    var result = new byte[length];
    NeoBookCartoonFile.Magic.CopyTo(result);
    BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(NeoBookCartoonFile.Magic.Length), (uint)offset);
    picture.CopyTo(result.AsSpan(offset));

    return result;
  }
}
