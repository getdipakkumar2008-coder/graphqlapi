# Architecture

The original JavaScript API remains at the repository root. The independently
runnable .NET implementation is under `dotnet/WeatherApi`.

```text
HTTP client -> ASP.NET Core endpoints -> GraphQL.NET schema/resolvers
                                              |
                                      IWeatherProvider
                                        /          \
                             SeedWeatherProvider   LiveWeatherProvider
                                     |               |       |        |
                               data/seed.json   geocoding forecast  AQI / NWS
```

`Program.cs` configures the selected provider, dependency injection, HTTP
routes, request-size/shape validation and GraphQL execution. `WeatherSchema.cs`
defines the original schema and validates field arguments. A travel weather
query first resolves a location; its child resolvers call the provider
independently, so omitted fields do not trigger upstream requests. GraphQL.NET
handles field selection, variables, errors and non-null propagation.

`Models.cs` defines the provider interface and response records.
`SeedWeatherProvider` reads the shared JSON file and calculates local dates via
`TimeProvider` and timezone data. `LiveWeatherProvider` uses a shared,
eight-second-timeout `HttpClient`, maps Open-Meteo and NWS responses, sets NWS
headers, and translates upstream connectivity and HTTP failures into
client-visible GraphQL errors. Other unexpected errors use GraphQL.NET's normal
error handling. No upstream calls are made for alerts outside the US.

`Docs.cs` exposes an OpenAPI description and CDN-hosted Swagger UI for the
single GraphQL endpoint. `dotnet/WeatherApi.Tests` exercises the HTTP and
GraphQL contract with seed data and a mocked HTTP handler for live responses;
it does not require access to external weather providers.
