# Mars Rover Photo Downloader: API + console runner in one small image (bonus deliverable).
# The NASA key is NEVER baked in: pass it at run time with  -e NASA__ApiKey=...
#
#   docker build -t marsphotos .
#   docker run --rm -p 5180:5180 -e NASA__ApiKey=$NASA_KEY -v "$PWD/photos:/app/photos" marsphotos
#   docker run --rm -e NASA__ApiKey=$NASA_KEY -v "$PWD/photos:/app/photos" --entrypoint dotnet marsphotos cli/MarsPhotos.Cli.dll

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY global.json Directory.Build.props .editorconfig MarsPhotos.sln ./
COPY src/MarsPhotos.Domain/MarsPhotos.Domain.csproj src/MarsPhotos.Domain/
COPY src/MarsPhotos.Application/MarsPhotos.Application.csproj src/MarsPhotos.Application/
COPY src/MarsPhotos.Infrastructure/MarsPhotos.Infrastructure.csproj src/MarsPhotos.Infrastructure/
COPY src/MarsPhotos.Api/MarsPhotos.Api.csproj src/MarsPhotos.Api/
COPY src/MarsPhotos.Cli/MarsPhotos.Cli.csproj src/MarsPhotos.Cli/
RUN dotnet restore src/MarsPhotos.Api/MarsPhotos.Api.csproj && dotnet restore src/MarsPhotos.Cli/MarsPhotos.Cli.csproj
COPY src/ src/
COPY specs/001-mars-photo-downloader/contracts/openapi.yaml specs/001-mars-photo-downloader/contracts/openapi.yaml
RUN dotnet publish src/MarsPhotos.Api/MarsPhotos.Api.csproj -c Release -o /out/api --no-restore \
 && dotnet publish src/MarsPhotos.Cli/MarsPhotos.Cli.csproj -c Release -o /out/cli --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /out/api ./
COPY --from=build /out/cli ./cli
COPY dates.txt ./dates.txt
# Non-root user (provided by the base image) and a writable photos folder, which is meant to be a volume.
RUN mkdir -p /app/photos && chown -R app:app /app/photos
USER app
ENV ASPNETCORE_URLS=http://+:5180 \
    Run__OutputRoot=/app/photos \
    Run__InputFile=/app/dates.txt
VOLUME ["/app/photos"]
EXPOSE 5180
ENTRYPOINT ["dotnet", "MarsPhotos.Api.dll"]
