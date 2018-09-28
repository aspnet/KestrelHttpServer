# Dockerization of Kestrel

## What is Kestrel?
Cross-platform web server for ASP.NET Core. 

## [Kestrel Repository](https://github.com/trilogy-group/KestrelHttpServer)
https://github.com/trilogy-group/KestrelHttpServer

## Docker Requirements
 1. Docker version 18.06.1-ce
 2. Docker compose version 1.22.0

## Version of container dependencies used 
1. [ubuntu:18.04](https://github.com/tianon/docker-brew-ubuntu-core/blob/222130dfdfa777c09a17b3f08ba68c5b9850e905/bionic/Dockerfile)

## Artifacts
1. [.dockerignore](.dockerignore) - 
Allows only [docker-start.sh](docker-start.sh) and [docker-nginx.conf](docker-nginx.conf).  Nothing else from the repo is needed during the docker build.

2. [docker-start.sh](docker-start.sh)
    * Build the current repo using [build.sh](build.sh). This is part of the repo and recommended way to build the repo as mentioned in the [aspnet github documentation](readme.md).
    * Starts the nginx reverse proxy.
    * Starts the Sample app using dotnet runtime downloaded by build.sh

3. [docker-nginx.conf](docker-nginx.conf) - This is the configuration file for [nginx](https://www.nginx.com/). Sets up a reverse proxy on the localhost:5001 to *:50001 / 0.0.0.0:50001. This is needed as all the samples present by default run on localhost:5001 and without source code change they will not be visible outside the docker container.

4. [Dockerfile](Dockerfile) - Has instructions for the docker image to be built. 
    * Installs the dependencies. As the recommended way is to run build.sh which downloads the dotnet runtime needed to build.
    * Installs [nginx](https://www.nginx.com/)

5. [docker-compose.yml](docker-compose.yml) - This is the docker-compose file. 
    * It creats the volume dotnet-root which is used with the only server present (kestrel). This is needed otherwise all the artifacts generated during the download of dotnet runtime and building the product are lost and docker-compose up downloads and build them everytime. 
    * Sets the container port 50001 mapping to host port 50001. Though the available samples run on port 5001. Since they are listening only on the loopback address (127.0.0.1). A reverse proxy using nginx forwards all the requests from http://*:50001 to http://127.0.0.1:5001

## Using Dockerization Artifacts
1. Copy all the artifcats in the repository directory
2. `cd path/to/repository/directory`
3. `docker-compose up` - this will build and run `command` as mentioned in the [docker-compose.yml](docker-compose.yml) file. 
4. navigate to http://localhost:50001
