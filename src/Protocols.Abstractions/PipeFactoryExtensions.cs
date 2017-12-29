using System.Buffers;

namespace System.IO.Pipelines
{
    public static class PipeFactory
    {
        public static (IPipeConnection Transport, IPipeConnection Application) CreateConnectionPair(MemoryPool memoryPool)
        {
            var options = new PipeOptions(memoryPool);
            return CreateConnectionPair(options, options);
        }

        public static (IPipeConnection Transport, IPipeConnection Application) CreateConnectionPair(PipeOptions inputOptions, PipeOptions outputOptions)
        {
            var input = new Pipe(inputOptions);
            var output = new Pipe(outputOptions);

            var transportToApplication = new PipeConnection(output.Reader, input.Writer);
            var applicationToTransport = new PipeConnection(input.Reader, output.Writer);

            return (applicationToTransport, transportToApplication);
        }
    }
}
