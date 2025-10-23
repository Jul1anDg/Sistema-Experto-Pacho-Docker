# ===== Build stage =====
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
# ✅ Habilitar globalización y locales
ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false
ENV LANG=es_ES.UTF-8
ENV LC_ALL=es_ES.UTF-8
WORKDIR /src
 
# Copia csproj y restaura
COPY *.sln ./
COPY *.csproj ./
RUN dotnet restore
 
# Copia todo y compila
COPY . .
WORKDIR /src
RUN dotnet build -c Release -o /app/build
RUN dotnet publish -c Release -o /app/publish --no-restore
 
# ===== Runtime stage =====
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
# ✅ Habilitar globalización y locales
ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false
ENV LANG=es_ES.UTF-8
ENV LC_ALL=es_ES.UTF-8
WORKDIR /app
COPY --from=build /app/publish .
# expone el puerto que usa tu app (cámbialo si tu app usa otro)
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
ENTRYPOINT ["dotnet", "Pacho.dll"]