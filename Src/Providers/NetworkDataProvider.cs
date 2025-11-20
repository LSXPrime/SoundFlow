using System.Buffers;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using SoundFlow.Abstracts;
using SoundFlow.Enums;
using SoundFlow.Interfaces;
using SoundFlow.Metadata;
using SoundFlow.Metadata.Models;
using SoundFlow.Structs;
using SoundFlow.Utils;

namespace SoundFlow.Providers;

/// <summary>
///     Provides audio data from an internet source, supporting both direct audio URLs and HLS (m3u(8)) playlists.
/// </summary>
/// <remarks>
///     Note: Initialization is performed asynchronously. The provider may not be ready to produce data immediately
///     after the constructor returns. Methods will return 0 or default values until initialization is complete
/// </remarks>
public sealed class NetworkDataProvider : ISoundDataProvider
{
    private readonly HttpClient _httpClient;
    private volatile NetworkDataProviderBase? _actualProvider;
    private bool _initializationFailed;
    private readonly ReadOptions _defaultReadOptions = new()
    {
        ReadTags = false,
        ReadAlbumArt = false,
        DurationAccuracy = DurationAccuracy.FastEstimate
    };

    /// <summary>
    ///     Initializes a new instance of the <see cref="NetworkDataProvider" /> class, automatically detecting the audio format.
    ///     This begins the process of downloading and preparing the stream. Not recommended for HLS streams.
    /// </summary>
    /// <param name="engine">The audio engine instance.</param>
    /// <param name="url">The URL of the audio stream.</param>
    /// <param name="options">Optional configuration for metadata reading.</param>
    public NetworkDataProvider(AudioEngine engine, string url, ReadOptions? options = null)
    {
        _httpClient = new HttpClient();
        SampleRate = 0; // Will be determined during initialization
        _ = InitializeInternalAsync(engine, null, null, url ?? throw new ArgumentNullException(nameof(url)), options ?? _defaultReadOptions);
    }

    /// <summary>
    ///     Initializes a new instance of the <see cref="NetworkDataProvider" /> class with a specified format.
    ///     This is required for HLS streams where the segment format must be known in advance.
    /// </summary>
    /// <param name="engine">The audio engine instance.</param>
    /// <param name="format">The audio format containing channels and sample rate and sample format.</param>
    /// <param name="url">The URL of the audio stream.</param>
    /// <param name="hlsSegmentFormatId">For HLS streams, the format identifier (e.g., "aac", "mp3") of the individual segments. This is ignored for direct streams.</param>
    public NetworkDataProvider(AudioEngine engine, AudioFormat format, string url, string? hlsSegmentFormatId = null)
    {
        _httpClient = new HttpClient();
        SampleRate = format.SampleRate;
        _ = InitializeInternalAsync(engine, format, hlsSegmentFormatId, url ?? throw new ArgumentNullException(nameof(url)), _defaultReadOptions);
    }

    /// <inheritdoc />
    public int Position => _actualProvider?.Position ?? 0;

    /// <inheritdoc />
    public int Length => _actualProvider?.Length ?? 0;

    /// <inheritdoc />
    public bool CanSeek => _actualProvider?.CanSeek ?? false;

    /// <inheritdoc />
    public SampleFormat SampleFormat => _actualProvider?.SampleFormat ?? SampleFormat.Unknown;

    /// <inheritdoc />
    public int SampleRate { get; private set; }

    /// <inheritdoc />
    public bool IsDisposed { get; private set; }
    
    /// <inheritdoc />
    public SoundFormatInfo? FormatInfo => _actualProvider?.FormatInfo;

    /// <inheritdoc />
    public event EventHandler<EventArgs>? EndOfStreamReached;

    /// <inheritdoc />
    public event EventHandler<PositionChangedEventArgs>? PositionChanged;

    private async Task InitializeInternalAsync(AudioEngine engine, AudioFormat? format, string? hlsSegmentFormatId, string url, ReadOptions? options)
    {
        try
        {
            var isHls = await IsHlsUrlAsync(url);

            NetworkDataProviderBase provider = isHls
                ? new HlsStreamProvider(engine, format, hlsSegmentFormatId, url, _httpClient)
                : new DirectStreamProvider(engine, format, url, _httpClient, options);

            await provider.InitializeAsync();
            
            // Wire up events from the internal provider to this facade's events
            provider.EndOfStreamReached += (_, e) => EndOfStreamReached?.Invoke(this, e);
            provider.PositionChanged += (_, e) => PositionChanged?.Invoke(this, e);
            
            // Update the public sample rate after initialization
            SampleRate = provider.SampleRate;

            // The provider is ready, assign it. This is the "go-live" signal.
            _actualProvider = provider;
        }
        catch (Exception ex)
        {
            // If anything fails during initialization, mark it and clean up.
            Log.Error($"NetworkDataProvider failed to initialize for URL '{url}': {ex.Message}");
            _initializationFailed = true;
            EndOfStreamReached?.Invoke(this, EventArgs.Empty);
            Dispose();
        }
    }

    /// <inheritdoc />
    public int ReadBytes(Span<float> buffer)
    {
        if (IsDisposed) return 0;
        
        // Return 0 if the provider isn't ready yet, or initialization failed, to supply silence until the stream is ready.
        if (_actualProvider == null)
        {
            if (_initializationFailed)
            {
                EndOfStreamReached?.Invoke(this, EventArgs.Empty);
            }
            return 0;
        }
        
        return _actualProvider.ReadBytes(buffer);
    }

    /// <inheritdoc />
    public void Seek(int sampleOffset)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);

        if (_actualProvider != null)
            _actualProvider.Seek(sampleOffset);
        else // If called before initialization, seeking is not yet supported.
            throw new InvalidOperationException("Cannot seek: The stream is not yet initialized.");
    }
    
    /// <inheritdoc />
    public void Dispose()
    {
        if (IsDisposed) return;
        
        IsDisposed = true;
        _actualProvider?.Dispose();
        _httpClient.Dispose();
        GC.SuppressFinalize(this);
    }
    
    private static async Task<bool> IsHlsUrlAsync(string url)
    {
        try
        {
            if (url.EndsWith(".m3u8", StringComparison.OrdinalIgnoreCase) || url.EndsWith(".m3u", StringComparison.OrdinalIgnoreCase)) return true;

            var request = new HttpRequestMessage(HttpMethod.Head, url);
            // Use a new temp client for this static check to not interfere with the instance client's lifecycle
            using var client = new HttpClient();
            var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);

            if (response is { IsSuccessStatusCode: true, Content.Headers.ContentType: not null })
            {
                var contentType = response.Content.Headers.ContentType.MediaType!;
                if (contentType.Equals("application/vnd.apple.mpegurl", StringComparison.OrdinalIgnoreCase) ||
                    contentType.Equals("application/x-mpegURL", StringComparison.OrdinalIgnoreCase) ||
                    contentType.Equals("audio/x-mpegURL", StringComparison.OrdinalIgnoreCase) ||
                    contentType.Equals("audio/mpegurl", StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            var content = await DownloadPartialContentAsync(client, url, 1024);
            return content != null && content.Contains("#EXT", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }
    
    private static async Task<string?> DownloadPartialContentAsync(HttpClient client, string url, int byteCount)
    {
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Range = new RangeHeaderValue(0, byteCount - 1);
            var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
            
            // If the server doesn't support partial content or playlist file is too small, retry with the full content
            if (response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable)
            {
                request = new HttpRequestMessage(HttpMethod.Get, url);
                response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
            }
            
            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content.ReadAsStreamAsync();
            var buffer = new byte[byteCount];
            var bytesRead = await stream.ReadAsync(buffer);
            return Encoding.UTF8.GetString(buffer, 0, bytesRead);
        }
        catch
        {
            return null;
        }
    }
}

/// <summary>
///     Internal abstract base class for network providers.
/// </summary>
internal abstract class NetworkDataProviderBase(AudioEngine engine, AudioFormat? format, string url, HttpClient client)
    : ISoundDataProvider
{
    protected readonly AudioEngine Engine = engine;
    protected AudioFormat? UserProvidedFormat = format;
    protected readonly string Url = url;
    protected readonly HttpClient HttpClient = client;
    protected readonly object Lock = new();
    protected int SamplePosition;

    public abstract Task InitializeAsync();
    
    public abstract int ReadBytes(Span<float> buffer);
    public abstract void Seek(int sampleOffset);
    
    public int Position => SamplePosition;
    public int Length { get; protected set; }
    public bool CanSeek { get; protected set; }
    public SampleFormat SampleFormat { get; protected set; }
    public int SampleRate { get; protected set; }
    public bool IsDisposed { get; private set; }
    public SoundFormatInfo? FormatInfo { get; protected set; }

    public event EventHandler<EventArgs>? EndOfStreamReached;
    public event EventHandler<PositionChangedEventArgs>? PositionChanged;

    protected virtual void OnEndOfStreamReached() => EndOfStreamReached?.Invoke(this, EventArgs.Empty);
    protected virtual void OnPositionChanged(int newPosition) => PositionChanged?.Invoke(this, new PositionChangedEventArgs(newPosition));

    public virtual void Dispose()
    {
        if (IsDisposed) return;
        IsDisposed = true;
    }
}

/// <summary>
///     Handles direct audio streams (e.g., MP3, WAV, OGG files).
///     Uses a background buffering strategy for large files to prevent network issues from crashing the audio thread.
/// </summary>
internal sealed class DirectStreamProvider(AudioEngine engine, AudioFormat? format, string url, HttpClient client, ReadOptions? readOptions)
    : NetworkDataProviderBase(engine, format, url, client)
{
    private ISoundDecoder? _decoder;
    private Stream? _stream;

    // Files smaller than 50 MB will be downloaded to memory to allow seeking.
    private const long MaxMemoryDownloadSize = 50 * 1024 * 1024;

    public override async Task InitializeAsync()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, Url);
        var response = await HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();

        var contentLength = response.Content.Headers.ContentLength;

        // Download to memory if we know the content length, and it's smaller than the threshold.
        if (contentLength is < MaxMemoryDownloadSize)
        {
            using (response)
            {
                var ms = new MemoryStream();
                await response.Content.CopyToAsync(ms);
                ms.Position = 0;
                _stream = ms;
            }
        }
        else
        {
            // For large or chunked streams, use a buffered stream.
            var bufferedStream = new BufferedNetworkStream();
            bufferedStream.StartProducerTask(response); // Starts the background download.
            _stream = bufferedStream;
        }

        var formatInfoResult = SoundMetadataReader.Read(_stream, readOptions);

        if (formatInfoResult is { IsSuccess: true, Value: not null })
        {
            FormatInfo = formatInfoResult.Value;
            var formatToUse = UserProvidedFormat ?? new AudioFormat
            {
                Format = SampleFormat.F32,
                Channels = FormatInfo.ChannelCount,
                SampleRate = FormatInfo.SampleRate,
                Layout = AudioFormat.GetLayoutFromChannels(FormatInfo.ChannelCount)
            };
            _stream.Position = 0;
            _decoder = Engine.CreateDecoder(_stream, FormatInfo.FormatIdentifier, formatToUse);
        }
        else
        {
            _stream.Position = 0;
            _decoder = Engine.CreateDecoder(_stream, out var detectedFormat, UserProvidedFormat);
            FormatInfo = new SoundFormatInfo
            {
                FormatName = "Unknown (Probed)",
                FormatIdentifier = "unknown",
                ChannelCount = detectedFormat.Channels,
                SampleRate = detectedFormat.SampleRate,
                Duration = _decoder.Length > 0 && detectedFormat.SampleRate > 0
                    ? TimeSpan.FromSeconds((double)_decoder.Length / (detectedFormat.SampleRate * detectedFormat.Channels))
                    : TimeSpan.Zero
            };
        }

        SampleFormat = _decoder.SampleFormat;
        SampleRate = _decoder.SampleRate;
        Length = FormatInfo != null ? (int)(FormatInfo.Duration.TotalSeconds * SampleRate * FormatInfo.ChannelCount) : _decoder.Length;
        CanSeek = _stream.CanSeek;
    }

    public override int ReadBytes(Span<float> buffer)
    {
        if (IsDisposed || _decoder == null) return 0;
        
        var samplesRead = _decoder.Decode(buffer);
        if (samplesRead == 0)
        {
            OnEndOfStreamReached();
        }
        else
        {
            lock (Lock)
            {
                SamplePosition += samplesRead;
                OnPositionChanged(SamplePosition);
            }
        }
        return samplesRead;
    }

    public override void Seek(int sampleOffset)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        if (!CanSeek) throw new NotSupportedException("Seeking is not supported for this stream.");
        if (_decoder == null) return;
        lock (Lock)
        {
            _decoder.Seek(sampleOffset);
            SamplePosition = sampleOffset;
            OnPositionChanged(SamplePosition);
        }
    }

    public override void Dispose()
    {
        if (IsDisposed) return;
        lock(Lock)
        {
            if (IsDisposed) return;
            base.Dispose();
            _decoder?.Dispose();
            _stream?.Dispose();
        }
    }
}

/// <summary>
///     Handles HLS (HTTP Live Streaming) playlists (m3u8).
/// </summary>
internal sealed class HlsStreamProvider(AudioEngine engine, AudioFormat? format, string? segmentFormatId, string url, HttpClient client)
    : NetworkDataProviderBase(engine, format, url, client)
{
    private class HlsSegment
    {
        public string Uri { get; init; } = string.Empty;
        public double Duration { get; init; }
    }
    
    private readonly Queue<float> _audioBuffer = new();
    private bool _isEndOfStream;
    private CancellationTokenSource? _cancellationTokenSource;

    private readonly List<HlsSegment> _hlsSegments = [];
    private int _currentSegmentIndex;
    private bool _isEndList;
    private double _hlsTotalDuration;
    private double _hlsTargetDuration = 5;
    private string? _segmentFormatId = segmentFormatId;

    public override async Task InitializeAsync()
    {
        _cancellationTokenSource = new CancellationTokenSource();
        await DownloadAndParsePlaylistAsync(Url, _cancellationTokenSource.Token);

        if (_hlsSegments.Count == 0)
            throw new InvalidOperationException("No segments found in HLS playlist.");
        
        await DetermineSegmentFormatAsync(_cancellationTokenSource.Token);
        SampleFormat = SampleFormat.F32; // Decoded HLS is typically float
        SampleRate = UserProvidedFormat!.Value.SampleRate;
        Length = _isEndList ? (int)(_hlsTotalDuration * SampleRate) : -1;
        CanSeek = _isEndList;

        // Start background buffering
        _ = BufferHlsStreamAsync(_cancellationTokenSource.Token);
    }
    
    
    private async Task DetermineSegmentFormatAsync(CancellationToken cancellationToken)
    {
        var firstSegmentUrl = _hlsSegments[0].Uri;
        
        // Download just the first few KB of the first segment to identify it.
        var request = new HttpRequestMessage(HttpMethod.Get, firstSegmentUrl);
        request.Headers.Range = new RangeHeaderValue(0, 8192); // 8 KB is plenty for any audio header.
        
        using var response = await HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var segmentHeaderStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        
        // The metadata reader needs a seekable stream.
        var memoryStream = new MemoryStream();
        await segmentHeaderStream.CopyToAsync(memoryStream, cancellationToken);
        memoryStream.Position = 0;

        using var decoder = Engine.CreateDecoder(memoryStream, out var detectedFormat, UserProvidedFormat);
        
        _segmentFormatId = "unknown_probed"; // The actual format ID is not crucial, as the decoder succeeded.
        UserProvidedFormat = detectedFormat;
    }
    
    public override int ReadBytes(Span<float> buffer)
    {
        if (IsDisposed) return 0;

        var attempts = 0;
        const int maxAttempts = 50; // ~5 seconds at 100ms intervals
        var samplesRead = 0;

        lock (Lock)
        {
            while (samplesRead < buffer.Length && attempts < maxAttempts)
            {
                if (_audioBuffer.Count > 0)
                {
                    buffer[samplesRead++] = _audioBuffer.Dequeue();
                }
                else if (_isEndOfStream)
                {
                    if (samplesRead == 0)
                        OnEndOfStreamReached();
                    break;
                }
                else
                {
                    attempts++;
                    Monitor.Wait(Lock, TimeSpan.FromMilliseconds(100));
                }
            }
        }
        
        if (samplesRead > 0)
        {
            lock (Lock)
            {
                SamplePosition += samplesRead;
                OnPositionChanged(SamplePosition);
            }
        }
    
        return samplesRead;
    }
    
    public override void Seek(int sampleOffset)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);

        if (!CanSeek)
            throw new NotSupportedException("Seeking is not supported for this stream.");

        var targetTime = sampleOffset / (double)SampleRate;
        double cumulativeTime = 0;
        var newSegmentIndex = 0;
        foreach (var segment in _hlsSegments)
        {
            if (cumulativeTime + segment.Duration >= targetTime)
                break;
            
            cumulativeTime += segment.Duration;
            newSegmentIndex++;
        }

        if (newSegmentIndex >= _hlsSegments.Count)
            newSegmentIndex = _hlsSegments.Count > 0 ? _hlsSegments.Count - 1 : 0;
        
        lock (Lock)
        {
            _cancellationTokenSource?.Cancel();
            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = new CancellationTokenSource();
            
            _audioBuffer.Clear();
            _isEndOfStream = false;
            SamplePosition = sampleOffset;
            _currentSegmentIndex = newSegmentIndex;
            
            _ = BufferHlsStreamAsync(_cancellationTokenSource.Token);
            OnPositionChanged(SamplePosition);
        }
    }
    
    private async Task BufferHlsStreamAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!IsDisposed && !cancellationToken.IsCancellationRequested)
            {
                if (!_isEndList && ShouldRefreshPlaylist())
                {
                    await DownloadAndParsePlaylistAsync(Url, cancellationToken);
                }

                if (_currentSegmentIndex < _hlsSegments.Count)
                {
                    var segment = _hlsSegments[_currentSegmentIndex];
                    await DownloadAndBufferSegmentAsync(segment, cancellationToken);
                    _currentSegmentIndex++;
                }
                else if (_isEndList)
                {
                    lock (Lock)
                    {
                        _isEndOfStream = true;
                        Monitor.PulseAll(Lock);
                    }
                    break; 
                }
                else
                {
                    await Task.Delay(TimeSpan.FromSeconds(_hlsTargetDuration / 2), cancellationToken);
                }
            }
        }
        catch (OperationCanceledException) { /* Expected on seek/dispose */ }
        catch
        {
            lock (Lock)
            {
                _isEndOfStream = true;
                Monitor.PulseAll(Lock);
            }
        }
    }
    
    private bool ShouldRefreshPlaylist()
    {
        if (_isEndList) return false;
        var timeUntilEnd = _hlsSegments.Skip(_currentSegmentIndex).Sum(s => s.Duration);
        return timeUntilEnd < _hlsTargetDuration * 1.5; // Refresh if we have less than 1.5 segments left
    }
    
    private async Task DownloadAndBufferSegmentAsync(HlsSegment segment, CancellationToken cancellationToken)
    {
        using var response = await HttpClient.GetAsync(segment.Uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var segmentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var decoder = Engine.CreateDecoder(segmentStream, out _, UserProvidedFormat!.Value);

        var buffer = ArrayPool<float>.Shared.Rent(8192);
        try
        {
            while (!IsDisposed && !cancellationToken.IsCancellationRequested)
            {
                var samplesRead = decoder.Decode(buffer);
                if (samplesRead <= 0) break;

                lock (Lock)
                {
                    for (var i = 0; i < samplesRead; i++)
                        _audioBuffer.Enqueue(buffer[i]);
                    Monitor.PulseAll(Lock);
                }
            }
        }
        finally
        {
            ArrayPool<float>.Shared.Return(buffer);
        }
    }
    
    private async Task DownloadAndParsePlaylistAsync(string playlistUrl, CancellationToken cancellationToken)
    {
        var response = await HttpClient.GetAsync(playlistUrl, cancellationToken);
        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        
        lock (Lock)
        {
            ParseHlsPlaylist(content, playlistUrl);
        }
    }

    private void ParseHlsPlaylist(string playlistContent, string baseUrl)
    {
        var lines = playlistContent.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var newSegments = new List<HlsSegment>();
        var newTotalDuration = 0.0;
        
        double segmentDuration = 0;

        foreach (var line in lines)
        {
            var trimmedLine = line.Trim();
            if (string.IsNullOrEmpty(trimmedLine)) continue;

            if (trimmedLine.StartsWith("#EXT-X-TARGETDURATION", StringComparison.OrdinalIgnoreCase))
            {
                if (double.TryParse(trimmedLine["#EXT-X-TARGETDURATION:".Length..], out var duration))
                    _hlsTargetDuration = duration;
            }
            else if (trimmedLine.StartsWith("#EXTINF", StringComparison.OrdinalIgnoreCase))
            {
                if (double.TryParse(trimmedLine["#EXTINF:".Length..].Split(',')[0], out var duration))
                    segmentDuration = duration;
            }
            else if (trimmedLine.StartsWith("#EXT-X-ENDLIST", StringComparison.OrdinalIgnoreCase))
            {
                _isEndList = true;
            }
            else if (!trimmedLine.StartsWith('#'))
            {
                var segmentUri = CombineUri(baseUrl, trimmedLine);
                newSegments.Add(new HlsSegment { Uri = segmentUri, Duration = segmentDuration });
                newTotalDuration += segmentDuration;
            }
        }

        if (!_isEndList)
        {
            var existingUris = new HashSet<string>(_hlsSegments.Select(s => s.Uri));
            var segmentsToAdd = newSegments.Where(s => !existingUris.Contains(s.Uri)).ToList();
            if (segmentsToAdd.Count != 0)
            {
                _hlsSegments.AddRange(segmentsToAdd);
                _hlsTotalDuration += segmentsToAdd.Sum(s => s.Duration);
            }
        }
        else
        {
            _hlsSegments.Clear();
            _hlsSegments.AddRange(newSegments);
            _hlsTotalDuration = newTotalDuration;
        }
    }
    
    private static string CombineUri(string baseUri, string relativeUri)
    {
        return Uri.TryCreate(new Uri(baseUri), relativeUri, out var newUri) ? newUri.ToString() : relativeUri;
    }
    
    public override void Dispose()
    {
        if (IsDisposed) return;
        lock (Lock)
        {
            if (IsDisposed) return;
            base.Dispose();
            _cancellationTokenSource?.Cancel();
            _cancellationTokenSource?.Dispose();
            _audioBuffer.Clear();
        }
    }
}

/// <summary>
///     A thread-safe, in-memory stream that acts as a circular buffer between a producer (network download)
///     and a consumer (audio decoder). It blocks reads when empty and writes when full.
/// </summary>
internal sealed class BufferedNetworkStream(int bufferSize = 1 * 1024 * 1024) : Stream
{
    private enum DownloadState { Buffering, Completed, Failed }

    private readonly byte[] _buffer = new byte[bufferSize];
    private int _writePosition;
    private int _readPosition;
    private int _bytesAvailable;

    private readonly object _lock = new();
    private volatile DownloadState _state = DownloadState.Buffering;
    private CancellationTokenSource? _cts;
    private Task? _producerTask;
    private bool _isDisposed;
    
    /// <summary>
    /// Starts the background producer task that reads from the network response and fills the buffer.
    /// This method takes ownership of the HttpResponseMessage.
    /// </summary>
    /// <param name="sourceResponse">The HTTP response message containing the content stream.</param>
    public void StartProducerTask(HttpResponseMessage sourceResponse)
    {
        _cts = new CancellationTokenSource();
        _producerTask = Task.Run(async () =>
        {
            var tempBuffer = ArrayPool<byte>.Shared.Rent(16384); // 16KB read buffer
            try
            {
                await using var sourceStream = await sourceResponse.Content.ReadAsStreamAsync(_cts.Token);
                while (!_cts.IsCancellationRequested)
                {
                    var bytesRead = await sourceStream.ReadAsync(tempBuffer, _cts.Token);
                    if (bytesRead == 0) break; // End of network stream
                    
                    Write(tempBuffer, 0, bytesRead);
                }

                if (!_cts.IsCancellationRequested) SignalCompletion();
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Network error occurred.
                SignalFailure();
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(tempBuffer);
                sourceResponse.Dispose();
            }
        });
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        lock (_lock)
        {
            while (_bytesAvailable == 0 && _state == DownloadState.Buffering && !_isDisposed)
            {
                Monitor.Wait(_lock);
            }

            if (_bytesAvailable == 0) return 0; // End of stream or failure

            var bytesToRead = Math.Min(count, _bytesAvailable);
            
            // Read from circular buffer
            var firstChunkSize = Math.Min(bytesToRead, _buffer.Length - _readPosition);
            Buffer.BlockCopy(_buffer, _readPosition, buffer, offset, firstChunkSize);
            _readPosition = (_readPosition + firstChunkSize) % _buffer.Length;

            if (firstChunkSize < bytesToRead)
            {
                var secondChunkSize = bytesToRead - firstChunkSize;
                Buffer.BlockCopy(_buffer, _readPosition, buffer, offset + firstChunkSize, secondChunkSize);
                _readPosition = (_readPosition + secondChunkSize) % _buffer.Length;
            }
            
            _bytesAvailable -= bytesToRead;
            Monitor.PulseAll(_lock); // Signal producer that space is available
            return bytesToRead;
        }
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        if (count == 0) return;

        lock (_lock)
        {
            while (_buffer.Length - _bytesAvailable < count && !_isDisposed)
            {
                Monitor.Wait(_lock);
            }
            
            if (_isDisposed) return;

            // Write to circular buffer
            var firstChunkSize = Math.Min(count, _buffer.Length - _writePosition);
            Buffer.BlockCopy(buffer, offset, _buffer, _writePosition, firstChunkSize);
            _writePosition = (_writePosition + firstChunkSize) % _buffer.Length;

            if (firstChunkSize < count)
            {
                var secondChunkSize = count - firstChunkSize;
                Buffer.BlockCopy(buffer, offset + firstChunkSize, _buffer, _writePosition, secondChunkSize);
                _writePosition = (_writePosition + secondChunkSize) % _buffer.Length;
            }

            _bytesAvailable += count;
            Monitor.PulseAll(_lock); // Signal consumer that data is available
        }
    }

    private void SignalCompletion()
    {
        lock (_lock)
        {
            _state = DownloadState.Completed;
            Monitor.PulseAll(_lock); // Wake any waiting readers to signal EOS
        }
    }

    private void SignalFailure()
    {
        lock (_lock)
        {
            _state = DownloadState.Failed;
            Monitor.PulseAll(_lock); // Wake any waiting readers to signal EOS
        }
    }
    
    protected override void Dispose(bool disposing)
    {
        if (_isDisposed) return;
        _isDisposed = true;

        if (disposing)
        {
            lock (_lock)
            {
                _cts?.Cancel();
                Monitor.PulseAll(_lock); // Unblock any waiting threads
            }
            _producerTask?.Wait(TimeSpan.FromSeconds(5)); // Wait for producer to finish
            _cts?.Dispose();
        }

        base.Dispose(disposing);
    }
    
    #region Not Supported Stream Members
    public override bool CanRead => !_isDisposed;
    public override bool CanSeek => false;
    public override bool CanWrite => !_isDisposed;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    #endregion
}