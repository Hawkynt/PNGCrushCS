using System;

namespace FileFormat.ZxMultiArtist;

/// <summary>Assembles ZX Spectrum MultiArtist file bytes from a <see cref="ZxMultiArtistFile"/>.</summary>
public static class ZxMultiArtistWriter {

  public static byte[] ToBytes(ZxMultiArtistFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var attributeSize = ZxMultiArtistFile.GetAttributeSize(file.Mode);
    var result = new byte[ZxMultiArtistFile.GetFileSize(file.Mode)];

    ZxMultiArtistFile.Signature.CopyTo(result);
    result[3] = 1;                       // the version every file carries
    result[4] = (byte)file.Mode;

    var at = ZxMultiArtistFile.HeaderSize;
    _Interleave(file.BitmapData, result.AsSpan(at));
    _Interleave(file.SecondBitmapData.Length == ZxMultiArtistFile.BitmapSize ? file.SecondBitmapData : file.BitmapData,
      result.AsSpan(at + ZxMultiArtistFile.BitmapSize));

    at += ZxMultiArtistFile.BitmapSize * 2;
    file.AttributeData.AsSpan(0, attributeSize).CopyTo(result.AsSpan(at));
    var secondAttributes = file.SecondAttributeData.Length == attributeSize ? file.SecondAttributeData : file.AttributeData;
    secondAttributes.AsSpan(0, attributeSize).CopyTo(result.AsSpan(at + attributeSize));

    return result;
  }

  /// <summary>Puts rows back into the order the Spectrum's screen holds them.</summary>
  private static void _Interleave(byte[] linear, Span<byte> target) {
    for (var y = 0; y < ZxMultiArtistReader.RowCount; ++y) {
      var third = y / 64;
      var characterRow = y % 64 / 8;
      var pixelLine = y % 8;
      var to = third * 2048 + pixelLine * 256 + characterRow * ZxMultiArtistReader.BytesPerRow;
      linear.AsSpan(y * ZxMultiArtistReader.BytesPerRow, ZxMultiArtistReader.BytesPerRow).CopyTo(target[to..]);
    }
  }
}
