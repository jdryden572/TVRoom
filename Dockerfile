# https://hub.docker.com/_/microsoft-dotnet
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /source

# install NPM, we need it to build the client app
RUN apt update
RUN apt install nodejs npm -y

# copy csproj and restore as distinct layers
COPY *.sln .
COPY TVRoom/TVRoom.csproj ./TVRoom/
COPY TVRoom.Tests/TVRoom.Tests.csproj ./TVRoom.Tests/
RUN dotnet restore

COPY . .
RUN dotnet publish TVRoom/TVRoom.csproj -c release -o /app --no-restore

# final stage/image
FROM lscr.io/linuxserver/ffmpeg:7.0.2

# Add Ubuntu backports repository https://learn.microsoft.com/en-us/dotnet/core/install/linux-ubuntu#register-the-ubuntu-net-backports-package-repository
RUN apt update
RUN apt-get install -y software-properties-common
RUN add-apt-repository -y ppa:dotnet/backports
# Install aspnetcore runtime
RUN apt-get install -y aspnetcore-runtime-9.0

WORKDIR /app
COPY --from=build /app ./
ENTRYPOINT ["dotnet", "TVRoom.dll"]