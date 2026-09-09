using System;
using System.Buffers.Binary;

namespace FileFormat.HalfLifeModel;

/// <summary>Writes the texture-only form of a Half-Life Studio Model v10 file.</summary>
public static class HalfLifeModelWriter {

  private const int _SkinReferenceSize = sizeof(short);

  public static byte[] ToBytes(HalfLifeModelFile file) {
    if (file.Width is < HalfLifeModelFile.MinWriteDimension or > HalfLifeModelFile.MaxWriteDimension
        || file.Height is < HalfLifeModelFile.MinWriteDimension or > HalfLifeModelFile.MaxWriteDimension)
      throw new ArgumentOutOfRangeException(nameof(file),
        $"Half-Life model textures must be between {HalfLifeModelFile.MinWriteDimension} and {HalfLifeModelFile.MaxWriteDimension} pixels on each side.");

    var pixelBytes = checked(file.Width * file.Height);
    if (file.PixelData is not { } pixels || pixels.Length != pixelBytes)
      throw new ArgumentException(
        $"A {file.Width}x{file.Height} Half-Life model texture requires exactly {pixelBytes} indexed pixels.", nameof(file));

    var paletteBytes = HalfLifeModelFile.PaletteEntries * 3;
    if (file.Palette is not { } palette || palette.Length != paletteBytes)
      throw new ArgumentException(
        $"A Half-Life model texture requires exactly {paletteBytes} palette bytes.", nameof(file));

    var tableAt = HalfLifeModelFile.HeaderSize;
    var skinAt = checked(tableAt + HalfLifeModelFile.TextureEntrySize);
    var dataAt = _Align4(checked(skinAt + _SkinReferenceSize));
    var length = _Align4(checked(dataAt + pixelBytes + paletteBytes));
    var result = new byte[length];

    HalfLifeModelFile.Signature.CopyTo(result);
    BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(4), HalfLifeModelFile.Version);
    BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(HalfLifeModelFile.LengthOffset), length);
    BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(HalfLifeModelFile.TextureCountOffset), 1);
    BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(HalfLifeModelFile.TextureIndexOffset), tableAt);
    BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(HalfLifeModelFile.TextureDataOffset), dataAt);
    BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(HalfLifeModelFile.SkinReferenceCountOffset), 1);
    BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(HalfLifeModelFile.SkinFamilyCountOffset), 1);
    BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(HalfLifeModelFile.SkinIndexOffset), skinAt);

    var entry = result.AsSpan(tableAt, HalfLifeModelFile.TextureEntrySize);
    _WriteName(entry[..HalfLifeModelFile.TextureNameLength], file.Name);
    BinaryPrimitives.WriteInt32LittleEndian(entry[64..], 0);
    BinaryPrimitives.WriteInt32LittleEndian(entry[68..], file.Width);
    BinaryPrimitives.WriteInt32LittleEndian(entry[72..], file.Height);
    BinaryPrimitives.WriteInt32LittleEndian(entry[76..], dataAt);

    BinaryPrimitives.WriteInt16LittleEndian(result.AsSpan(skinAt), 0);
    pixels.AsSpan().CopyTo(result.AsSpan(dataAt));
    palette.AsSpan().CopyTo(result.AsSpan(dataAt + pixelBytes));
    return result;
  }

  private static int _Align4(int value) => checked((value + 3) & ~3);

  private static void _WriteName(Span<byte> destination, string? name) {
    name = string.IsNullOrEmpty(name) ? "texture" : name;
    if (name.Length >= HalfLifeModelFile.TextureNameLength)
      throw new ArgumentException(
        $"A Half-Life model texture name must be shorter than {HalfLifeModelFile.TextureNameLength} ASCII characters.", nameof(name));

    for (var i = 0; i < name.Length; ++i) {
      var character = name[i];
      if (character is '\0' or > '\x7F')
        throw new ArgumentException("A Half-Life model texture name must contain only non-NUL ASCII characters.", nameof(name));

      destination[i] = (byte)character;
    }
  }
}
