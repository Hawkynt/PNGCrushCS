using System;

namespace FileFormat.CfliDesigner;

/// <summary>Assembles CFLI Designer (.cfli) file bytes from a CfliDesignerFile.</summary>
public static class CfliDesignerWriter {

  public static byte[] ToBytes(CfliDesignerFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var result = new byte[CfliDesignerFile.LoadAddressSize + file.RawData.Length];
    result[0] = (byte)(file.LoadAddress & 0xFF);
    result[1] = (byte)(file.LoadAddress >> 8);
    file.RawData.AsSpan(0, file.RawData.Length).CopyTo(result.AsSpan(CfliDesignerFile.LoadAddressSize));

    return result;
  }
}
