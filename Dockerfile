# https://hub.docker.com/_/microsoft-dotnet
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /source

# install NPM, we need it to build the client app
RUN apt update
RUN apt install nodejs npm -y

# copy csproj and restore as distinct layers
COPY *.sln .
COPY TVRoom/TVRoom.csproj .
COPY TVRoom.Tests/TVRoom.Tests.csproj .
RUN dotnet restore

COPY . .
RUN dotnet publish -c release -o /app --no-restore

# final stage/image
FROM lscr.io/linuxserver/ffmpeg:latest
RUN apt-get update
RUN apt-get install -y aspnetcore-runtime-8.0
WORKDIR /app
COPY --from=build /app ./
ENTRYPOINT ["dotnet", "TVRoom.dll"]