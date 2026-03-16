namespace FileFormat.Core;

/// <summary>Describes a single field within a binary file format header for hex editor field coloring.</summary>
public readonly record struct HeaderFieldDescriptor(string Name, int Offset, int Size);
