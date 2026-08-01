using System;
using System.IO;

namespace FileFormat.FalconFuckpaint;

/// <summary>Assembles a Falcon Fuckpaint picture: the palette, then the bitplanes.</summary>
public static class FalconFuckpaintWriter {

  public static byte[] ToBytes(FalconFuckpaintFile file) {
    ArgumentNullException.ThrowIfNull(file);
    return file.Data ?? [];
  }

  public static void ToFile(FalconFuckpaintFile file, FileInfo target) {
    ArgumentNullException.ThrowIfNull(target);
    File.WriteAllBytes(target.FullName, ToBytes(file));
  }
}
