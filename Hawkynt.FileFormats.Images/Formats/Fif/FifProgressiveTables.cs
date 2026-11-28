using System;
using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;

namespace FileFormat.Fif;

/// <summary>The fixed prefix-code books and coordinate tables of the progressive FIF1 format.</summary>
/// <remarks>
/// These are constants of the format, not of any one file: the 10136 bytes Iterated's DECO_32.DLL
/// 6.1 holds at 0x110310D0–0x11033867, copied verbatim (stored deflated here). Lookups keep the DLL's
/// addresses so they can be checked against it.
/// </remarks>
internal static class FifProgressiveTables {

  public const int BaseAddress = 0x110310D0;

  private const string _DEFLATED =
    "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" +
    "/ErT93R9/w8VO21M";

  public static readonly byte[] Data = _Inflate();

  private static byte[] _Inflate() {
    using var input = new MemoryStream(Convert.FromBase64String(_DEFLATED));
    using var zlib = new ZLibStream(input, CompressionMode.Decompress);
    using var output = new MemoryStream();
    zlib.CopyTo(output);
    var data = output.ToArray();
    if (data.Length != 0x2798)
      throw new InvalidOperationException("The embedded FIF1 tables are corrupt.");

    return data;
  }

  public static byte U8(int address) => Data[address - BaseAddress];

  public static sbyte S8(int address) => (sbyte)Data[address - BaseAddress];

  public static ushort U16(int address) => BinaryPrimitives.ReadUInt16LittleEndian(Data.AsSpan(address - BaseAddress));

  public static short S16(int address) => BinaryPrimitives.ReadInt16LittleEndian(Data.AsSpan(address - BaseAddress));
}
