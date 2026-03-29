namespace FileFormat.Dng;

/// <summary>Internal helper representing a single TIFF IFD entry during parsing.</summary>
internal readonly record struct DngIfdEntry(ushort Tag, ushort Type, uint Count, int ValueOffset);
