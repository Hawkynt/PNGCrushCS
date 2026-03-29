using System;
using System.IO;
using System.Text;

namespace FileFormat.AndrewToolkit;

/// <summary>Assembles Andrew Toolkit (ATK) file bytes from an <see cref="AndrewToolkitFile"/>.</summary>
public static class AndrewToolkitWriter {

  public static byte[] ToBytes(AndrewToolkitFile file) {
    ArgumentNullException.ThrowIfNull(file);

    using var ms = new MemoryStream();
    var header = Encoding.ASCII.GetBytes($"width = {file.Width}\nheight = {file.Height}\n\n");
    ms.Write(header, 0, header.Length);
    ms.Write(file.RawData, 0, file.RawData.Length);
    return ms.ToArray();
  }
}
