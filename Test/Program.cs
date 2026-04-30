using SoundFlow.Codecs.FFMpeg;

var factory = new FFmpegCodecFactory();
var stream = File.OpenRead(@"C:\Sync\Music\Neurotech\Symphonies\Neurotech - Symphonies - 02 The Halcyon Symphony.ogg");
var decoder = factory.TryCreateDecoder(stream, out var format);

decoder.Seek(50000);

Console.ReadLine();