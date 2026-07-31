namespace FileFormat.Wpg;

/// <summary>WPG record type identifiers.</summary>
public enum WpgRecordType : byte {
  BitmapType1 = 11,

  /// <summary>
  /// The palette record, which is 14 rather than the 12 it used to be given.
  /// </summary>
  /// <remarks>
  /// 12 is a different record entirely, so the colour map was never matched and never read: an
  /// 8-bit WPG decoded to indexed pixels with an empty palette, which is an image no caller can
  /// draw. The dimensions were right, which is why a check that only compared those did not notice.
  /// </remarks>
  ColorMap = 14,
  StartWpg = 15,
  EndWpg = 16,
  BitmapType2 = 20
}
