namespace Mangrove.Server.Storage.Smb;

/// <summary>
/// Seekable, read-only stream that pulls byte ranges from an SMB file on demand (spec §5:
/// "stream pages straight from the SMB Stream"). It never buffers the whole file. Each read is
/// serialized through the owning connection's gate because the SMB client is single-threaded.
/// </summary>
public sealed class SmbReadStream : Stream
{
    private readonly SmbConnection _connection;
    private readonly object _handle;
    private readonly long _length;
    private readonly int _chunkSize;
    private long _position;
    private bool _disposed;

    public SmbReadStream(SmbConnection connection, object handle, long length)
    {
        _connection = connection;
        _handle = handle;
        _length = length;
        _chunkSize = (int)Math.Min(connection.MaxReadSize == 0 ? 65536u : connection.MaxReadSize, int.MaxValue);
    }

    public override bool CanRead => true;
    public override bool CanSeek => true;
    public override bool CanWrite => false;
    public override long Length => _length;

    public override long Position
    {
        get => _position;
        set => _position = value;
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        if (count <= 0 || _position >= _length) return 0;

        var toRead = (int)Math.Min(Math.Min(count, _chunkSize), _length - _position);

        _connection.Gate.Wait();
        byte[] data;
        try
        {
            data = _connection.Read(_handle, _position, toRead);
        }
        finally
        {
            _connection.Gate.Release();
        }

        if (data.Length == 0) return 0;
        var copied = Math.Min(data.Length, count);
        Array.Copy(data, 0, buffer, offset, copied);
        _position += copied;
        return copied;
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        _position = origin switch
        {
            SeekOrigin.Begin => offset,
            SeekOrigin.Current => _position + offset,
            SeekOrigin.End => _length + offset,
            _ => _position,
        };
        return _position;
    }

    public override void Flush() { }
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            _disposed = true;
            _connection.Gate.Wait();
            try { _connection.CloseHandle(_handle); }
            finally { _connection.Gate.Release(); }
        }
        base.Dispose(disposing);
    }
}
