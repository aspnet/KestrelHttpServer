using System;
using System.Buffers;
using System.Collections.Generic;
using System.Text;

namespace System.IO.Pipelines
{
    public class DuplexPipe : IDuplexPipe
    {
        public DuplexPipe(PipeReader reader, PipeWriter writer)
        {
            Input = reader;
            Output = writer;
        }

        public PipeReader Input { get; }

        public PipeWriter Output { get; }

        public void Dispose()
        {
        }

        public static (IDuplexPipe Transport, IDuplexPipe Application) CreateConnectionPair(MemoryPool memoryPool)
        {
            return CreateConnectionPair(new PipeOptions(memoryPool), new PipeOptions(memoryPool));
        }

        public static (IDuplexPipe Transport, IDuplexPipe Application) CreateConnectionPair(PipeOptions inputOptions, PipeOptions outputOptions)
        {
            var input = new Pipe(inputOptions);
            var output = new Pipe(outputOptions);

            var transportToApplication = new DuplexPipe(output.Reader, input.Writer);
            var applicationToTransport = new DuplexPipe(input.Reader, output.Writer);

            return (applicationToTransport, transportToApplication);
        }
    }
}
