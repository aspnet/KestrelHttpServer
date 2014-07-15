FROM djsell/kvm:1.0.0-alpha3-10143

ADD . /opt/kestrel

# hopefully this will work some day
# kpm restore has issues so for now
# make sure you run kpm restore on your host machine
# before you run docker build
#RUN /bin/bash -c "source ~/.kre/kvm/kvm.sh && kpm restore"

WORKDIR /opt/kestrel/samples/SampleApp
EXPOSE 5000
CMD [ "k", "run" ]
