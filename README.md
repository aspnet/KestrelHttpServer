KestrelHttpServer
=================

This repo contains a development web server for ASP.NET vNext based on [libuv](https://github.com/joyent/libuv).

This project is part of ASP.NET vNext. You can find samples, documentation and getting started instructions for ASP.NET vNext at the [Home](https://github.com/aspnet/home) repo.

Docker
------
Run Kestrel with Docker
```
docker build -t myuser/kestrel:latest .
docker run --rm -it -p 5000:5000 myuser/kestrel:latest
```
