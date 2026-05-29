using System;
using System.Collections.Generic;
using FileFormat.Ico;

namespace FileFormat.Cur;

/// <summary>Assembles CUR file bytes from a <see cref="CurFile"/>.</summary>
public static class CurWriter {

  public static byte[] ToBytes(CurFile file) {
    ArgumentNullException.ThrowIfNull(file);

    // Convert CurImages to IcoImages for the shared assembler
    var icoImages = new List<IcoImage>(file.Images.Count);
    foreach (var image in file.Images)
      icoImages.Add(new IcoImage {
        Width = image.Width,
        Height = image.Height,
        BitsPerPixel = image.BitsPerPixel,
        Format = image.Format,
        Data = image.Data
      });

    var icoFile = new IcoFile { Images = icoImages };

    // Use IcoWriter's internal assembler with Cursor type and hotspot field override
    var images = file.Images;
    return IcoWriter._Assemble(icoFile, IcoFileType.Cursor, i => (images[i].HotspotX, images[i].HotspotY));
  }
}
