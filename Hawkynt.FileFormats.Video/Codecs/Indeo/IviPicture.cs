namespace FileFormat.Codecs.Indeo;

/// <summary>
/// One decoded Indeo picture: three sample planes with the chrominance ones a quarter the size in
/// each direction.
/// </summary>
/// <remarks>
/// Both formats code YVU9 and nothing else — sixteen luminance samples to each pair of chrominance
/// samples — which is the layout their era called 4:1:0 and the one both decoders' picture headers
/// refuse to depart from. The planes are what a decode is measured on; turning them into anything a
/// screen shows is a display convention and happens outside this.
/// </remarks>
/// <param name="Width">The picture's width in luminance samples.</param>
/// <param name="Height">The picture's height in luminance samples.</param>
/// <param name="ChromaWidth">The chrominance planes' width, which is the luminance width divided by four, rounded up.</param>
/// <param name="ChromaHeight">The chrominance planes' height, likewise.</param>
/// <param name="Luma">The luminance plane, one byte a sample, row-major.</param>
/// <param name="ChromaBlue">The blue-difference plane.</param>
/// <param name="ChromaRed">The red-difference plane.</param>
internal sealed record IviPicture(
  int Width,
  int Height,
  int ChromaWidth,
  int ChromaHeight,
  byte[] Luma,
  byte[] ChromaBlue,
  byte[] ChromaRed);
