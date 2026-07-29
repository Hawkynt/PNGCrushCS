using System;
using System.IO;

namespace FileFormat.BioRadPic;

/// <summary>Reads Bio-Rad PIC files from bytes, streams, or file paths.</summary>
public static class BioRadPicReader {

  public static BioRadPicFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Bio-Rad PIC file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static BioRadPicFile FromStream(Stream stream) {
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

  public static BioRadPicFile FromSpan(ReadOnlySpan<byte> data) {

    if (data.Length < BioRadPicHeader.StructSize)
      throw new InvalidDataException($"Data too small for a valid Bio-Rad PIC file: expected at least {BioRadPicHeader.StructSize} bytes, got {data.Length}.");

    var header = BioRadPicHeader.ReadFrom(data);

    if (header.FileId != BioRadPicHeader.MagicFileId)
      throw new InvalidDataException($"Invalid Bio-Rad PIC file_id: expected {BioRadPicHeader.MagicFileId}, got {header.FileId}.");

    var nx = header.Nx;
    var ny = header.Ny;
    var npic = header.Npic;

    if (nx == 0)
      throw new InvalidDataException("Invalid Bio-Rad PIC width: 0.");
    if (ny == 0)
      throw new InvalidDataException("Invalid Bio-Rad PIC height: 0.");
    if (npic == 0)
      throw new InvalidDataException("Invalid Bio-Rad PIC npic: 0.");

    var isByte = header.ByteFormat == 1;
    var bytesPerPixel = isByte ? 1 : 2;
    var firstImageSize = nx * ny * bytesPerPixel;

    if (data.Length < BioRadPicHeader.StructSize + firstImageSize)
      throw new InvalidDataException($"Data too small for pixel data: expected at least {BioRadPicHeader.StructSize + firstImageSize} bytes, got {data.Length}.");

    var pixelData = new byte[firstImageSize];
    data.Slice(BioRadPicHeader.StructSize, firstImageSize).CopyTo(pixelData.AsSpan(0));

    return new BioRadPicFile {
      Width = nx,
      Height = ny,
      NumImages = npic,
      ByteFormat = isByte,
      Name = header.Name ?? "",
      Lens = header.Lens,
      MagFactor = header.MagFactor,
      Ramp1Min = header.Ramp1Min,
      Ramp1Max = header.Ramp1Max,
      Ramp2Min = header.Ramp2Min,
      Ramp2Max = header.Ramp2Max,
      Color1 = header.Color1,
      Color2 = header.Color2,
      Merged = header.Merged,
      Edited = header.Edited,
      Notes = header.Notes,
      PixelData = pixelData,
    };
    }

  public static BioRadPicFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < BioRadPicHeader.StructSize)
      throw new InvalidDataException($"Data too small for a valid Bio-Rad PIC file: expected at least {BioRadPicHeader.StructSize} bytes, got {data.Length}.");

    var header = BioRadPicHeader.ReadFrom(data.AsSpan());

    if (header.FileId != BioRadPicHeader.MagicFileId)
      throw new InvalidDataException($"Invalid Bio-Rad PIC file_id: expected {BioRadPicHeader.MagicFileId}, got {header.FileId}.");

    var nx = header.Nx;
    var ny = header.Ny;
    var npic = header.Npic;

    if (nx == 0)
      throw new InvalidDataException("Invalid Bio-Rad PIC width: 0.");
    if (ny == 0)
      throw new InvalidDataException("Invalid Bio-Rad PIC height: 0.");
    if (npic == 0)
      throw new InvalidDataException("Invalid Bio-Rad PIC npic: 0.");

    var isByte = header.ByteFormat == 1;
    var bytesPerPixel = isByte ? 1 : 2;
    var firstImageSize = nx * ny * bytesPerPixel;

    if (data.Length < BioRadPicHeader.StructSize + firstImageSize)
      throw new InvalidDataException($"Data too small for pixel data: expected at least {BioRadPicHeader.StructSize + firstImageSize} bytes, got {data.Length}.");

    var pixelData = new byte[firstImageSize];
    data.AsSpan(BioRadPicHeader.StructSize, firstImageSize).CopyTo(pixelData.AsSpan(0));

    return new BioRadPicFile {
      Width = nx,
      Height = ny,
      NumImages = npic,
      ByteFormat = isByte,
      Name = header.Name ?? "",
      Lens = header.Lens,
      MagFactor = header.MagFactor,
      Ramp1Min = header.Ramp1Min,
      Ramp1Max = header.Ramp1Max,
      Ramp2Min = header.Ramp2Min,
      Ramp2Max = header.Ramp2Max,
      Color1 = header.Color1,
      Color2 = header.Color2,
      Merged = header.Merged,
      Edited = header.Edited,
      Notes = header.Notes,
      PixelData = pixelData,
    };
  }
}
