/// <summary>
/// Represents the data contained within the IHDR chunk.
/// </summary>
internal readonly record struct IhdrData(int Width, int Height, byte BitDepth, ColorType ColorType, byte CompressionMethod, byte FilterMethod, InterlaceMethod InterlaceMethod);
