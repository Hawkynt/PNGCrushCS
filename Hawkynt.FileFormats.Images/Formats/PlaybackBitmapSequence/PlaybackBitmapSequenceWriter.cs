using System;

namespace FileFormat.PlaybackBitmapSequence;

/// <summary>Writes the verified single-picture BMSWinPlay form.</summary>
public static class PlaybackBitmapSequenceWriter {

  public static byte[] ToBytes(PlaybackBitmapSequenceFile file) {
    if (file.Bitmap == null || file.Bitmap.Length < 2 || file.Bitmap[0] != (byte)'B' || file.Bitmap[1] != (byte)'M')
      throw new ArgumentException("Playback Bitmap Sequence requires a complete Windows BMP payload.", nameof(file));

    var output = new byte[checked(PlaybackBitmapSequenceFile.HeaderSize + file.Bitmap.Length)];
    PlaybackBitmapSequenceFile.Magic.CopyTo(output);
    file.Bitmap.CopyTo(output, PlaybackBitmapSequenceFile.HeaderSize);
    return output;
  }
}
