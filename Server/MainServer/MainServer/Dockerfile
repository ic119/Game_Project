# 빌드 컨텍스트는 이 파일의 상위 폴더인 Server/MainServer 여야 한다.
# docker-compose.yml에서 build.context: . / build.dockerfile: MainServer/Dockerfile 로 이미 맞춰져 있다.

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY MainServer/MainServer.csproj MainServer/
RUN dotnet restore MainServer/MainServer.csproj

COPY MainServer/ MainServer/
# 장비 장착 검증에 GameServer의 아이템 정의를 함께 쓴다(MainServer.csproj가 이 경로를 링크한다).
COPY GameServer/Items/ItemDefinitions.json GameServer/Items/
RUN dotnet publish MainServer/MainServer.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
COPY --from=build /app/publish .

EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
ENTRYPOINT ["dotnet", "MainServer.dll"]
