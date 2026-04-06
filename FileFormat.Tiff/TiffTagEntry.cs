namespace FileFormat.Tiff;

/// <summary>Parsed IFD tag entry.</summary>
internal readonly record struct TiffTagEntry(ushort Tag, ushort Type, uint Count, uint ValueOrOffset);
