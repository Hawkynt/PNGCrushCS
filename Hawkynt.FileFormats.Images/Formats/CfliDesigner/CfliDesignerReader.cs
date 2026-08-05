using System;
using System.Buffers.Binary;
using System.IO;

namespace FileFormat.CfliDesigner;

/// <summary>Reads CFLI Designer pictures from bytes, streams, or file paths.</summary>
/// <remarks>
/// The matrices are page-strided: each takes 1024 bytes of address space for the 1000 it uses, and
/// the last is written without its padding, which is what makes a file 8170 rather than 8194.
/// </remarks>
public static class CfliDesignerReader {

  public static CfliDesignerFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("CFLI Designer file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static CfliDesignerFile FromStream(Stream stream) {
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

  public static CfliDesignerFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < CfliDesignerFile.ExpectedFileSize)
      throw new InvalidDataException(
        $"A CFLI Designer picture is {CfliDesignerFile.ExpectedFileSize} bytes; this file is {data.Length}.");

    var screens = new byte[CfliDesignerFile.ScreenBankCount * CfliDesignerFile.ScreenBankSize];
    for (var bank = 0; bank < CfliDesignerFile.ScreenBankCount; ++bank)
      data.Slice(
          CfliDesignerFile.LoadAddressSize + bank * CfliDesignerFile.ScreenBankStride,
          CfliDesignerFile.ScreenBankSize)
        .CopyTo(screens.AsSpan(bank * CfliDesignerFile.ScreenBankSize));

    return new() {
      LoadAddress = BinaryPrimitives.ReadUInt16LittleEndian(data),
      Screens = screens,
    };
  }

  public static CfliDesignerFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
