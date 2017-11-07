#!/usr/bin/env bash
DIR="$( cd "$( dirname "${BASH_SOURCE[0]}" )" && pwd )"

docker run -p 5000:5000 --rm kestrel-http2-sample
