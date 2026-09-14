# 빌드 컨텍스트는 이 파일의 상위 폴더인 Server/MainServer 여야 한다(Shared 프로젝트를 함께 복사해야 하므로).
# docker-compose.yml에서 build.context: . / build.dockerfile: GameServer/Dockerfile 로 이미 맞춰져 있다.

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY Shared/Shared.csproj Shared/
COPY GameServer/GameServer.csproj GameServer/
RUN dotnet restore GameServer/GameServer.csproj

COPY Shared/ Shared/
COPY GameServer/ GameServer/
RUN dotnet publish GameServer/GameServer.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/runtime:8.0 AS final
WORKDIR /app
COPY --from=build /app/publish .

EXPOSE 9000
ENTRYPOINT ["dotnet", "GameServer.dll"]
