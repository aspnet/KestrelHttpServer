dotnet publish --framework netcoreapp2.0 "$PsScriptRoot/../Http2SampleApp.csproj"

docker build -t kestrel-http2-sample "$PsScriptRoot/.."
