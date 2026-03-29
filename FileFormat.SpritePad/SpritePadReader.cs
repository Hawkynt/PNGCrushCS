using System;
using System.IO;

namespace FileFormat.SpritePad;

/// <summary>Reads SpritePad (.spd) files from bytes, streams, or file paths.</summary>
public static class SpritePadReader {

  public static SpritePadFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("SpritePad file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static SpritePadFile FromStream(Stream stream) {
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

  public static SpritePadFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < SpritePadFile.V1HeaderSize + SpritePadFile.BytesPerSprite)
      throw new InvalidDataException($"Data too small for a valid SpritePad file (expected at least {SpritePadFile.V1HeaderSize + SpritePadFile.BytesPerSprite} bytes, got {data.Length}).");

    var version = data[0];
    var spriteCount = data[1];
    var multicolorFlag = data[2] != 0;

    int headerSize;
    byte[] extraHeader;

    if (version >= 2 && data.Length >= SpritePadFile.V2HeaderSize + SpritePadFile.BytesPerSprite) {
      headerSize = SpritePadFile.V2HeaderSize;
      extraHeader = new byte[SpritePadFile.V2HeaderSize - SpritePadFile.V1HeaderSize];
      data.AsSpan(SpritePadFile.V1HeaderSize, extraHeader.Length).CopyTo(extraHeader.AsSpan());
    } else {
      headerSize = SpritePadFile.V1HeaderSize;
      extraHeader = [];
    }

    var payloadSize = data.Length - headerSize;
    var rawData = new byte[payloadSize];
    data.AsSpan(headerSize, payloadSize).CopyTo(rawData.AsSpan());

    // Correct sprite count if header value is wrong
    var maxSprites = payloadSize / SpritePadFile.BytesPerSprite;
    if (spriteCount == 0 || spriteCount > maxSprites)
      spriteCount = (byte)Math.Min(maxSprites, 255);

    return new() {
      Version = version,
      SpriteCount = spriteCount,
      IsMulticolor = multicolorFlag,
      ExtraHeader = extraHeader,
      RawData = rawData,
    };
  }
}
