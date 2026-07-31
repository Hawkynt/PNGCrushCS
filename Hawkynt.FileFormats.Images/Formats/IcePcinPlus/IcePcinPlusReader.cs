using System;
using System.IO;
using FileFormat.AtariIce;

namespace FileFormat.IcePcinPlus;

/// <summary>Reads ICE PCIN+ pictures from bytes, streams, or file paths.</summary>
public static class IcePcinPlusReader {

  public static IcePcinPlusFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Picture not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static IcePcinPlusFile FromStream(Stream stream) {
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

  public static IcePcinPlusFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length != IcePcinPlusFile.FileSize || data[0] != 1)
      throw new InvalidDataException("Not an ICE PCIN+ picture.");

    // The first field is mode 12: the background and the four playfield registers, the playfield
    // ones taken from every other byte because the bytes between belong to the second field.
    var registers = new byte[9];
    registers[8] = (byte)(data[1] & 254);
    for (var i = 0; i < 4; ++i)
      registers[4 + i] = (byte)(data[5 + i * 2] & 254);

    var first = new IceField {
      CharactersOffset = IcePcinPlusFile.ScreenOffset,
      FontOffset = 14,
      Mode = IceFrameMode.Gr12,
      Registers = registers[..],
    };

    // The second is GTIA 10, which reads all nine registers, so the four sprite colours come into
    // play and the background is taken from a byte of its own.
    for (var i = 0; i < 4; ++i) {
      registers[i] = (byte)(data[1 + i] & 254);
      registers[4 + i] = (byte)(data[6 + i * 2] & 254);
    }

    registers[8] = (byte)(data[13] & 254);

    var second = new IceField {
      CharactersOffset = IcePcinPlusFile.ScreenOffset,
      FontOffset = 1038,
      Mode = IceFrameMode.Gr0Gtia10,

      // GTIA 10 starts two pixels later than the mode it is paired with, which the picture was
      // drawn expecting rather than something to correct.
      LeftSkip = 2,
      Registers = registers[..],
    };

    return new() { Data = data.ToArray(), Fields = [first, second] };
  }

  public static IcePcinPlusFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
