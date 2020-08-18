FROM microsoft/dotnet:3.0-sdk AS installer-env

COPY . /src/dotnet-function-app
RUN cd /src/dotnet-function-app && \
    mkdir -p /home/site/wwwroot && \
    dotnet publish *.csproj --output /home/site/wwwroot

# To enable ssh & remote debugging on app service change the base image to the one below
# FROM mcr.microsoft.com/azure-functions/dotnet:3.0-appservice 
FROM mcr.microsoft.com/azure-functions/dotnet:3.0
ENV AzureWebJobsScriptRoot=/home/site/wwwroot \
    AzureFunctionsJobHost__Logging__Console__IsEnabled=true

COPY --from=installer-env ["/home/site/wwwroot", "/home/site/wwwroot"]

# get latest python & blender related dependencies
RUN apt-get update && apt-get install -y --no-install-recommends apt-utils python3 python3-virtualenv \
python3-dev python3-pip libx11-6 libxi6 libxxf86vm1 libxfixes3 libxrender1 unzip wget bzip2 xz-utils \
&& rm -rf /var/lib/apt/lists/*

# get the dependencies for the script
RUN mkdir -p /local/
ADD scripts /local/scripts/ 

# get the blender 2.81a and setup the paths
RUN cd /tmp && wget -q https://download.blender.org/release/Blender2.83/blender-2.83.4-linux64.tar.xz \
&& ls -al \
&& tar xvf /tmp/blender-2.83.4-linux64.tar.xz -C /usr/bin/ \
&& rm -r /tmp/blender-2.83.4-linux64.tar.xz

# copy the shared lib for blender
RUN cp /usr/bin/blender-2.83.4-linux64/lib/lib* /usr/local/lib/ && ldconfig

# Entry point for dis.co
#WORKDIR /local/
 
# Test to see if we can run the script
# RUN /usr/bin/blender-2.83.4-linux64/blender -b -E CYCLES -P background_job.py -- --text="Hello World" --render="hello"
# CMD ["/usr/bin/blender-2.83.4-linux64/blender", "-b", "--version"]

# ENTRYPOINT [ "executable" ]
# ENTRYPOINT ["/usr/bin/blender-2.83.4-linux64/blender", "-b", "--version"]
