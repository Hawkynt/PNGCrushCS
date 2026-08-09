using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;

namespace FileFormat.HalfLifeModel;

/// <summary>Reads the skins out of Half-Life models from bytes, streams, or file paths.</summary>
public static class HalfLifeModelReader {

  private const int _MaxDimension = 8192;
  private const int _MaxSkins = 4096;

  public static HalfLifeModelFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Half-Life model not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static HalfLifeModelFile FromStream(Stream stream) {
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

  public static HalfLifeModelFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }

  public static HalfLifeModelFile FromSpan(ReadOnlySpan<byte> data) => FromSpan(data, -1);

  /// <summary>Reads one of the model's skins; a negative index means the last, which is the one the converter draws.</summary>
  public static HalfLifeModelFile FromSpan(ReadOnlySpan<byte> data, int skin) {
    if (data.Length < HalfLifeModelFile.MinFileSize)
      throw new InvalidDataException(
        $"Data too small for a Half-Life model (at least {HalfLifeModelFile.MinFileSize} bytes are needed, got {data.Length}).");

    if (!data[..HalfLifeModelFile.Signature.Length].SequenceEqual(HalfLifeModelFile.Signature))
      throw new InvalidDataException("Not a Half-Life model: it does not open with IDST.");

    if (BinaryPrimitives.ReadInt32LittleEndian(data[4..]) != HalfLifeModelFile.Version)
      throw new InvalidDataException("Not a Half-Life model this reads: the version is not ten.");

    var count = BinaryPrimitives.ReadInt32LittleEndian(data[HalfLifeModelFile.TextureCountOffset..]);
    var tableAt = BinaryPrimitives.ReadInt32LittleEndian(data[HalfLifeModelFile.TextureIndexOffset..]);
    var dataAt = BinaryPrimitives.ReadInt32LittleEndian(data[HalfLifeModelFile.TextureDataOffset..]);

    // All three have to be there. A model with no skins is a model with no picture in it, and the
    // converter refuses it rather than drawing something empty.
    if (count <= 0 || tableAt <= 0 || dataAt <= 0)
      throw new InvalidDataException($"A Half-Life model states {count} skins at {tableAt}, so it carries no picture.");

    if (count > _MaxSkins)
      throw new InvalidDataException($"A Half-Life model states {count} skins, which is more than this reads.");

    var tableBytes = (long)count * HalfLifeModelFile.TextureEntrySize;
    if (tableAt + tableBytes > data.Length)
      throw new InvalidDataException("A Half-Life model's texture table reaches past the end of the file.");

    var wanted = skin < 0 ? count - 1 : skin;
    if (wanted >= count)
      throw new ArgumentOutOfRangeException(nameof(skin), skin, $"The model carries {count} skins.");

    var entry = data.Slice(tableAt + wanted * HalfLifeModelFile.TextureEntrySize, HalfLifeModelFile.TextureEntrySize);
    var name = Encoding.ASCII.GetString(entry[..HalfLifeModelFile.TextureNameLength]).TrimEnd('\0');
    var width = BinaryPrimitives.ReadInt32LittleEndian(entry[68..]);
    var height = BinaryPrimitives.ReadInt32LittleEndian(entry[72..]);
    var pixelsAt = BinaryPrimitives.ReadInt32LittleEndian(entry[76..]);

    if (width < 1 || height < 1 || width > _MaxDimension || height > _MaxDimension)
      throw new InvalidDataException($"A Half-Life model's skin states a size of {width}x{height}.");

    var needed = (long)width * height + HalfLifeModelFile.PaletteEntries * 3;
    if (pixelsAt < 0 || pixelsAt + needed > data.Length)
      throw new InvalidDataException("A Half-Life model's skin reaches past the end of the file.");

    var pixels = data.Slice(pixelsAt, width * height).ToArray();
    var palette = data.Slice(pixelsAt + width * height, HalfLifeModelFile.PaletteEntries * 3).ToArray();

    return new() {
      Width = width,
      Height = height,
      SkinCount = count,
      Name = name,
      PixelData = pixels,
      Palette = palette,
    };
  }
}
