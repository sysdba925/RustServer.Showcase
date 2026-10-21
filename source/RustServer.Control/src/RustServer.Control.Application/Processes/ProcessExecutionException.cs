namespace RustServer.Control.Application.Processes;

public sealed class ProcessExecutionException : Exception
{
    public ProcessExecutionException(string fileName, Exception innerException)
        : base($"Unable to start external process '{fileName}'. {innerException.Message}", innerException)
    {
        FileName = fileName;
    }

    public string FileName { get; }
}
