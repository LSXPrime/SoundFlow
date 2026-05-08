using SoundFlow.Codecs.FFMpeg;
using System.Collections;
using System.Runtime.InteropServices;

foreach (var ext in new[] { "wav", "wma", "ape", "aac", "aif", "mp3", "flac", "ogg"})
{
    Console.WriteLine(ext);

    var factory = new FFmpegCodecFactory();
    var stream = File.OpenRead(@$"D:\Temp\TestSimplex.{ext}");
    var decoder = factory.TryCreateDecoder(stream, out var format);

    Console.WriteLine($"Length: {decoder.Length}");

    var chunk = new float[8192 * 2 * 4];
    //decoder.Seek(0);

    /*decoder.Decode(chunk);

    File.WriteAllBytes("Decode.raw", MemoryMarshal.Cast<float, byte>(chunk.AsSpan()));

    for (int i = 0; i < chunk.Length; i++)
        fullDecode.WriteLine($"[{i}] {chunk[i]:F4}");*/

    const int CHUNK_LENGTH = 1000;

    Span<float> data = new float[chunk.Length];

    for (int i = 0; i < 32; i++)
    {
        decoder.PreciseSeek(1000 * i);
        decoder.Decode(data.Slice(1000 * i, CHUNK_LENGTH));
    }

    //decoder.PreciseSeek(16000);
    //decoder.Decode(data.Slice(16000));

    File.WriteAllBytes($"DecodeSeek{ext.ToUpper()}.raw", MemoryMarshal.Cast<float, byte>(data));
}

return;