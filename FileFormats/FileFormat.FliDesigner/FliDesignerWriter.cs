using System;

namespace FileFormat.FliDesigner;

/// <summary>Assembles FLI Designer (.fd2) file bytes from a FliDesignerFile.</summary>
public static class FliDesignerWriter {

  public static byte[] ToBytes(FliDesignerFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var result = new byte[FliDesignerFile.LoadAddressSize + file.RawData.Length];
    result[0] = (byte)(file.LoadAddress & 0xFF);
    result[1] = (byte)(file.LoadAddress >> 8);
    file.RawData.AsSpan(0, file.RawData.Length).CopyTo(result.AsSpan(FliDesignerFile.LoadAddressSize));

    return result;
  }
}
