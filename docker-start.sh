#!/usr/bin/env bash
/data/build.sh /p:SkipTests=true
nginx -c /etc/docker-nginx.conf
dotnet $1