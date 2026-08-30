using System;
using System.IO;

namespace FileFormat.Ybm;

/// <summary>Writes Bennet Yee face-file bitmaps (YBM).</summary>
public static class YbmWriter {

  public static byte[] ToBytes(YbmFile file) {
    YbmFile.Validate(file, nameof(file));

    var data = new byte[checked(6 + file.RasterData.Length)];
    data[0] = 0x21;
    data[1] = 0x21;
    data[2] = (byte)(file.Width >> 8);
    data[3] = (byte)file.Width;
    data[4] = (byte)(file.Height >> 8);
    data[5] = (byte)file.Height;
    file.RasterData.CopyTo(data, 6);
    return data;
  }

  public static void ToStream(YbmFile file, Stream stream) {
    ArgumentNullException.ThrowIfNull(stream);
    stream.Write(ToBytes(file));
  }

  public static void ToFile(YbmFile file, FileInfo target) {
    ArgumentNullException.ThrowIfNull(target);
    File.WriteAllBytes(target.FullName, ToBytes(file));
  }
}
