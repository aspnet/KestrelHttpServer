// Unsure of header

using System.Threading;
using System.Threading.Tasks;

namespace System.IO
{
    public interface IBlockingReader<T>
    {
        int Read(T[] buffer, int offset, int count);
    }

    public interface IBlockingWriter<T>
    {
        void Flush();
        void Write(T value);
        void Write(T[] buffer, int offset, int count);
    }

    public interface ISeekable
    {
        long Position { get; set; }
        long Length { get; }
        long Seek(long offset, SeekOrigin origin);
    }

    public interface ISizable
    {
        void SetLength(long value);
    }

    public interface IReaderAsync<T>
    {
        ValueTask<int> ReadAsync(T[] buffer, int offset, int count);
        ValueTask<int> ReadAsync(T[] buffer, int offset, int count, CancellationToken cancellationToken);
    }

    public interface IWriterAsync<T>
    {
        Task FlushAsync();
        Task FlushAsync(CancellationToken cancellationToken);
        Task WriteAsync(T[] buffer, int offset, int count);
        Task WriteAsync(T[] buffer, int offset, int count, CancellationToken cancellationToken);
    }

    public interface IBlockingDuplex<T> : IBlockingReader<T>, IBlockingWriter<T> { }
    public interface ISeekableReader<T> : IBlockingReader<T>, ISeekable { }
    public interface ISeekableWriter<T> : IBlockingWriter<T>, ISeekable, ISizable { }
    public interface ISeekableDuplex<T> : ISeekableReader<T>, ISeekableWriter<T> { }

    public interface IDuplexAsync<T> : IReaderAsync<T>, IWriterAsync<T> { }

    public interface IBlockingReadStream<T> : IBlockingReader<T>, IDisposable { }
    public interface IBlockingWriteStream<T> : IBlockingWriter<T>, IDisposable { }
    public interface IBlockingDuplexStream<T> : IBlockingDuplex<T>, IBlockingReadStream<T>, IBlockingWriteStream<T> { }

    public interface ISeekableStream : ISeekable, IDisposable { }
    public interface ISeekableReadStream<T> : IBlockingReadStream<T>, ISeekableStream, ISeekableReader<T> { }
    public interface ISeekableWriteStream<T> : IBlockingWriteStream<T>, ISeekableStream, ISeekableWriter<T> { }
    public interface ISeekableDuplexStream<T> : ISeekableReadStream<T>, ISeekableWriteStream<T>, IBlockingDuplexStream<T>, ISeekableDuplex<T> { }

    public interface IReadStreamAsync<T> : IReaderAsync<T>, IDisposable { }
    public interface IWriteStreamAsync<T> : IWriterAsync<T>, IDisposable { }
    public interface IDuplexStreamAsync<T> : IReadStreamAsync<T>, IWriteStreamAsync<T>, IDuplexAsync<T> { }

    public interface IBlockingAsyncReader<T> : IReaderAsync<T>, IBlockingReader<T> { }
    public interface IBlockingAsyncWriter<T> : IWriterAsync<T>, IBlockingWriter<T> { }
    public interface IBlockingAsyncDuplex<T> : IDuplexAsync<T>, IBlockingDuplex<T> { }

    public interface IBlockingAsyncReadStream<T> : IReadStreamAsync<T>, IBlockingReadStream<T>, IBlockingAsyncReader<T> { }
    public interface IBlockingAsyncWriteStream<T> : IWriteStreamAsync<T>, IBlockingWriteStream<T>, IBlockingAsyncWriter<T> { }
    public interface IBlockingAsyncDuplexStream<T> : IDuplexStreamAsync<T>, IBlockingDuplexStream<T>, IBlockingAsyncDuplex<T>, IBlockingAsyncReadStream<T>, IBlockingAsyncWriteStream<T> { }
}
