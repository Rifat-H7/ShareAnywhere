# syntax=docker/dockerfile:1

FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:8.0-alpine AS build

# Set the working directory directly to where the files will be copied
WORKDIR /source

# Copy only the project and solution files to the working directory.
COPY ./src/ShareAnywhere/ShareAnywhere/*.csproj .

# Restore dependencies
RUN dotnet restore

# Copy the rest of the project files
COPY ./src/ShareAnywhere/ShareAnywhere/ .

# Run the publish command
ARG TARGETARCH
RUN --mount=type=cache,id=nuget,target=/root/.nuget/packages \
    dotnet publish -a ${TARGETARCH/amd64/x64} --use-current-runtime --self-contained false -o /app

FROM mcr.microsoft.com/dotnet/aspnet:8.0-alpine AS final
WORKDIR /app

COPY --from=build /app .

ENV ASPNETCORE_URLS=http://+:${PORT}

ENTRYPOINT ["dotnet", "ShareAnywhere.dll"]