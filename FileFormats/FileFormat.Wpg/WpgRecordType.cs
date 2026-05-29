namespace FileFormat.Wpg;

/// <summary>WPG record type identifiers.</summary>
public enum WpgRecordType : byte {
  BitmapType1 = 11,
  ColorMap = 12,
  StartWpg = 15,
  EndWpg = 16,
  BitmapType2 = 20
}
