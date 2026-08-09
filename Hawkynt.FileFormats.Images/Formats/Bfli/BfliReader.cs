using System;
using System.IO;

namespace FileFormat.Bfli;

/// <summary>Reads BFLI (.bfl/.bfli/.flp) files from bytes, streams, or file paths.</summary>
public static class BfliReader {

  public static BfliFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("BFLI file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static BfliFile FromStream(Stream stream) {
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

  /// <summary>Takes a file apart, or refuses it.</summary>
  /// <remarks>
  /// Three things have to hold and all three come from the reference readers: the length is exactly
  /// 33795 and nothing else, the two bytes of load address are 0x3BFF, and the byte behind them is
  /// <c>b</c>. RECOIL asks for the length and the <c>b</c>; XnView asks for all three. This asks for
  /// all three as well, because the format has no other header, and a load address alone would take
  /// any Commodore file that happened to load there.
  /// <para/>
  /// What this replaces took any file over nine thousand bytes long, so it accepted whatever it was
  /// handed under a name it claims and drew it as a 320 by 200 picture — a fax among them.
  /// </remarks>
  public static BfliFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length != BfliFile.FileSize)
      throw new InvalidDataException(
        $"Not a BFLI picture: one is exactly {BfliFile.FileSize} bytes and this is {data.Length}.");

    var loadAddress = (ushort)(data[0] | (data[1] << 8));
    if (loadAddress != BfliFile.LoadAddress || data[2] != BfliFile.Marker)
      throw new InvalidDataException(
        $"Not a BFLI picture: it opens ${loadAddress:X4} 0x{data[2]:X2}, and one opens ${BfliFile.LoadAddress:X4} 0x{BfliFile.Marker:X2}.");

    var payload = new byte[BfliFile.PayloadSize];
    data.Slice(BfliFile.HeaderSize, BfliFile.PayloadSize).CopyTo(payload);

    return new() { RawData = payload };
  }

  public static BfliFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
