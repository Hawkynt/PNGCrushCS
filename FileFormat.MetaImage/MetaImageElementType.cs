namespace FileFormat.MetaImage;

/// <summary>Describes the element (pixel sample) data type used in a MetaImage file.</summary>
public enum MetaImageElementType {
  /// <summary>Unsigned 8-bit integer (MET_UCHAR).</summary>
  MetUChar,
  /// <summary>Signed 16-bit integer (MET_SHORT).</summary>
  MetShort,
  /// <summary>Unsigned 16-bit integer (MET_USHORT).</summary>
  MetUShort,
  /// <summary>32-bit IEEE 754 float (MET_FLOAT).</summary>
  MetFloat,
}
