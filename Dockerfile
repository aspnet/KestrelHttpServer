FROM ubuntu:18.04

RUN apt-get update &&\
    apt-get install -y liblttng-ust0 libcurl3 libssl1.0.0 libkrb5-3 \
    zlib1g libicu60 unzip wget python3 libunwind-dev\
    nginx

VOLUME [ "/root" ]
COPY docker-start.sh docker-nginx.conf /etc/
RUN chmod 777 /etc/docker-start.sh
WORKDIR /data
ENV PATH=$PATH:/root/.dotnet
CMD /etc/docker-start.sh --help
