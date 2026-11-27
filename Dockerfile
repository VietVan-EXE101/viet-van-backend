FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ["VietVan.API.csproj", "./"]
RUN dotnet restore "VietVan.API.csproj"
COPY . .
RUN dotnet publish "VietVan.API.csproj" -c Release --no-restore -o /out /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
ENV ASPNETCORE_ENVIRONMENT=Production \
    PORT=10000 \
    Storage__Provider=R2 \
    Swagger__Enabled=true \
    Email__Provider=Gmail
EXPOSE 10000
COPY --from=build --chown=app:app /out .
RUN mkdir -p /app/PrivateMedia /app/wwwroot/uploads/work-covers \
    && chown -R app:app /app/PrivateMedia /app/wwwroot/uploads
USER app
ENTRYPOINT ["dotnet", "VietVan.API.dll"]
