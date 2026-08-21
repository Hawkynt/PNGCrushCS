namespace FileFormat.Codecs.Mve;

/// <summary>One reconstructed Interplay video picture: eight-bit palette indices, one byte a pixel.</summary>
internal sealed class MveFrame {

  internal MveFrame(int width, int height) {
    this.Width = width;
    this.Height = height;
    this.Indices = new byte[width * height];
  }

  internal int Width { get; }

  internal int Height { get; }

  internal byte[] Indices { get; }

  internal void CopyFrom(MveFrame other) => System.Array.Copy(other.Indices, this.Indices, this.Indices.Length);
}
