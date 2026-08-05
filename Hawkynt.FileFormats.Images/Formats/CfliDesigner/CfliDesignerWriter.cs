using System;
using System.Buffers.Binary;

namespace FileFormat.CfliDesigner;

/// <summary>Assembles CFLI Designer picture bytes.</summary>
public static class CfliDesignerWriter {

  public static byte[] ToBytes(CfliDesignerFile file) {
    ArgumentNullException.ThrowIfNull(file.Screens);

    var result = new byte[CfliDesignerFile.ExpectedFileSize];
    BinaryPrimitives.WriteUInt16LittleEndian(result, file.LoadAddress);

    for (var bank = 0; bank < CfliDesignerFile.ScreenBankCount; ++bank) {
      var from = bank * CfliDesignerFile.ScreenBankSize;
      if (from >= file.Screens.Length)
        break;

      file.Screens
        .AsSpan(from, Math.Min(CfliDesignerFile.ScreenBankSize, file.Screens.Length - from))
        .CopyTo(result.AsSpan(CfliDesignerFile.LoadAddressSize + bank * CfliDesignerFile.ScreenBankStride));
    }

    return result;
  }
}
