namespace FileFormat.Codecs.Ea;

/// <summary>One reconstructed Electronic Arts CMV picture: eight-bit palette indices, one byte a pixel.</summary>
internal sealed class EaCmvFrame {

  internal EaCmvFrame(int width, int height) {
    this.Width = width;
    this.Height = height;
    this.Indices = new byte[width * height];
  }

  internal int Width { get; }

  internal int Height { get; }

  internal byte[] Indices { get; }
}
