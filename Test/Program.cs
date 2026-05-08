using SoundFlow.Codecs.FFMpeg;
using System.Collections;
using System.Runtime.InteropServices;

using var fullDecodeStream = File.OpenWrite("fulldecode.txt");
using var fullDecode = new StreamWriter(fullDecodeStream);
using var seekDecodeStream = File.OpenWrite("seekdecode.txt");
using var seekDecode = new StreamWriter(seekDecodeStream);

var factory = new FFmpegCodecFactory();
var stream = File.OpenRead(@"D:\Temp\TestSimplex.ogg");
var decoder = factory.TryCreateDecoder(stream, out var format);

var chunk = new float[8192 * 2 * 4];
//decoder.Seek(0);

/*decoder.Decode(chunk);

File.WriteAllBytes("Decode.raw", MemoryMarshal.Cast<float, byte>(chunk.AsSpan()));

for (int i = 0; i < chunk.Length; i++)
    fullDecode.WriteLine($"[{i}] {chunk[i]:F4}");*/

Span<float> data = new float[chunk.Length];

decoder.PreciseSeek(1000);
decoder.Decode(data.Slice(1000, 2000));

decoder.PreciseSeek(16000);
decoder.Decode(data.Slice(16000, 2000));


decoder.PreciseSeek(8000);
decoder.Decode(data.Slice(8000, 2000));

File.WriteAllBytes("DecodeSeek.raw", MemoryMarshal.Cast<float, byte>(data));

return;

int totalIndex = 0;

Span<float> dummy = stackalloc float[chunk.Length];

//Dictionary<int, int> mapping = new Dictionary<int, int>();

//bool seeked = false;

//const int SLICE_LENGTH = 1024;

//Span<float> sliceBuffer = stackalloc float[SLICE_LENGTH];

//int written = 0;

//int writePos = 0;

//do
//{
//    var frame = sliceBuffer.Slice(0, 2);

//    var newPos = decoder.Decode(frame);

//    if(newPos != -1)
//    {
//        newPos *= 2;
//        Console.WriteLine($"Pos corrected: {writePos} -> {newPos}");
//        writePos = newPos;
//    }

//    frame.CopyTo(data.Slice(writePos, 2));

//    written += 2;
//    writePos += 2;

//    if (!seeked && writePos > 4096)
//    {
//        decoder.Seek(20000);
//        Console.WriteLine("Seeking");
//        seeked = true;
//    }

//} while (writePos < data.Length);

//File.WriteAllBytes("DecodeContinuous.raw", MemoryMarshal.Cast<float, byte>(data));


Console.WriteLine();

//var keys = mapping.Values.ToArray();

//int written = 0;
//int index = 0;

//List<Range> ranges = new List<Range>();

//while (written < data.Length)
//{
//    var start = keys[index];
//    var next = keys[index + 1];

//    index++;

//    var count = next - start;

//    written += count * 2;

//    ranges.Add(new Range()
//    {
//        start = start,
//        length = count
//    });
//}

//ranges.Sort((a, b) => -a.start.CompareTo(b.start));

//foreach(var range in ranges)
//{
//    var slice = data.Slice(range.start * 2, Math.Min(range.length * 2, data.Length - (range.start * 2)));

//    decoder.Seek(range.start * 2);
//    var result = decoder.Decode(slice);

//    Console.WriteLine($"Seek to: {range.start}, Got: {result}, Writting: {slice.Length / 2}");
//}
//data.Fill(0);

//for(int off = data.Length - SLICE_LENGTH; off >= 0; off -= SLICE_LENGTH)
//{
//    // var slice = data.Slice(off, SLICE_LENGTH);

//    Console.WriteLine("Seeking to: " + off);

//    decoder.Seek(off);
//    var pos = decoder.Decode(sliceBuffer);
//    pos *= 2;

//    //var offset = off - pos;

//    sliceBuffer.CopyTo(data.Slice(pos));

//    Console.WriteLine($"Writing to position: {pos}");

//    //if (offset > 0)
//    //{
//    //    while (offset > slice.Length)
//    //    {
//    //        decoder.Decode(slice);
//    //        offset -= slice.Length;
//    //    }

//    //    var validSlice = slice.Slice(offset);
//    //    validSlice.CopyTo(slice);

//    //    var result2 = decoder.Decode(slice.Slice(0, offset));

//    //    Console.WriteLine($"Offset: {offset}. AfterResult: {result2}");
//    //}
//}

//File.WriteAllBytes("DecodeSeek.raw", MemoryMarshal.Cast<float, byte>(data));

/*foreach(var key in keys)
{
    decoder.Seek(key * 2);

    var pos = decoder.Decode(dummy);

    Console.WriteLine($"{key} == {pos}");
}*/

//for (int off = 0; off < data.Length / 2; off++)
//{
//    var targetPos = off * decoder.Channels;

//    var slice = data.Slice(targetPos, 2);

//    decoder.Seek(targetPos);

//    var start = decoder.Decode(slice);
//    int skip = 0;

//    while((start + skip) != off)
//    {
//        decoder.Decode(slice);
//        skip++;
//    }

//    Console.WriteLine($"{off} -> {start} + {skip}");

//    for (int i = 0; i < slice.Length; i++)
//        seekDecode.WriteLine($"[{totalIndex++}] {slice[i]:F4}");
//}

//File.WriteAllBytes("DecodeSeek.raw", MemoryMarshal.Cast<float, byte>(data));

struct Range
{
    public int start;
    public int length;
}