using System.Buffers;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using SoundFlow.Abstracts;
using SoundFlow.Enums;
using SoundFlow.Interfaces;
using SoundFlow.Structs;


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

    /// <summary>
    ///     Initializes a new instance of the <see cref="NetworkDataProvider" /> class.
    ///     This begins the process of downloading and preparing the stream.
    /// </summary>
    /// <param name="engine">The audio engine instance.</param>
    /// <param name="format">The audio format containing channels and sample rate and sample format</param>
    /// <param name="url">The URL of the audio stream.</param>
    public NetworkDataProvider(AudioEngine engine, AudioFormat format, string url)
    {
        _httpClient = new HttpClient();
        SampleRate = format.SampleRate;
        _ = InitializeInternalAsync(engine, format, url ?? throw new ArgumentNullException(nameof(url)));
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
    public int SampleRate { get; }

    /// <inheritdoc />
    public bool IsDisposed { get; private set; }

    /// <inheritdoc />
    public event EventHandler<EventArgs>? EndOfStreamReached;

    /// <inheritdoc />
    public event EventHandler<PositionChangedEventArgs>? PositionChanged;

    private async Task InitializeInternalAsync(AudioEngine engine, AudioFormat format, string url)
    {
        try
        {
            var isHls = await IsHlsUrlAsync(url);
            
            NetworkDataProviderBase provider = isHls
                ? new HlsStreamProvider(engine, format, url, _httpClient)
                : new DirectStreamProvider(engine, format, url, _httpClient);

            await provider.InitializeAsync();
            
            // Wire up events from the internal provider to this facade's events
            provider.EndOfStreamReached += (_, e) => EndOfStreamReached?.Invoke(this, e);
            provider.PositionChanged += (_, e) => PositionChanged?.Invoke(this, e);

            // The provider is ready, assign it. This is the "go-live" signal.
            _actualProvider = provider;
        }
        catch
        {
            // If anything fails during initialization, mark it and clean up.
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
internal abstract class NetworkDataProviderBase(AudioEngine engine, AudioFormat format, string url, HttpClient client)
    : ISoundDataProvider
{
    protected readonly AudioEngine Engine = engine;
    protected readonly AudioFormat Format = format;
    protected readonly string Url = url;
    protected readonly HttpClient HttpClient = client;
    protected readonly object Lock = new();
    protected int SamplePosition;

    public abstract Task InitializeAsync();
    
    public abstract int ReadBytes(Span<float> buffer);
    public abstract void Seek(int sampleOffset);
    

    public int Position
    {
        get
        {
            lock (Lock)
            {
                return SamplePosition;
            }
        }
    }
    
    public int Length { get; protected set; }
    public bool CanSeek { get; protected set; }
    public SampleFormat SampleFormat { get; protected set; }
    public int SampleRate { get; } = format.SampleRate;
    public bool IsDisposed { get; private set; }

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
/// </summary>
internal sealed class DirectStreamProvider(AudioEngine engine, AudioFormat format, string url, HttpClient client)
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
            // Otherwise, use the live network stream. Seeking will not be supported in this case.
            _stream = await response.Content.ReadAsStreamAsync();
        }
        
        _decoder = Engine.CreateDecoder(_stream, Format);
        SampleFormat = _decoder.SampleFormat;
        Length = _decoder.Length;
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
internal sealed class HlsStreamProvider(AudioEngine engine, AudioFormat format, string url, HttpClient client)
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

    public override async Task InitializeAsync()
    {
        _cancellationTokenSource = new CancellationTokenSource();
        await DownloadAndParsePlaylistAsync(Url, _cancellationTokenSource.Token);

        if (_hlsSegments.Count == 0)
            throw new InvalidOperationException("No segments found in HLS playlist.");
        
        SampleFormat = SampleFormat.F32; // Decoded HLS is typically float
        Length = _isEndList ? (int)(_hlsTotalDuration * SampleRate) : -1;
        CanSeek = _isEndList;

        // Start background buffering
        _ = BufferHlsStreamAsync(_cancellationTokenSource.Token);
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
        using var decoder = Engine.CreateDecoder(segmentStream, Format);

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