using System;

namespace FileFormat.Core;

/// <summary>Generic convenience API for reading and writing binary header types that implement <see cref="IBinarySerializable{TSelf}"/>.</summary>
public static class HeaderSerializer {

  /// <summary>Reads an instance of <typeparamref name="T"/> from the given byte span.</summary>
  public static T Read<T>(ReadOnlySpan<byte> source) where T : IBinarySerializable<T>
    => T.ReadFrom(source);

  /// <summary>Writes the given value to the destination span.</summary>
  public static void Write<T>(T value, Span<byte> destination) where T : IBinarySerializable<T>
    => value.WriteTo(destination);

  /// <summary>Returns the serialized size in bytes for type <typeparamref name="T"/>.</summary>
  public static int SizeOf<T>() where T : IBinarySerializable<T>
    => T.SerializedSize;
}
