namespace WeatherApi;

public static class Docs
{
    public const string Html = """
        <!doctype html>
        <html><head><meta charset="utf-8"><title>Swagger UI</title>
        <link rel="stylesheet" href="https://unpkg.com/swagger-ui-dist@5/swagger-ui.css"></head>
        <body><div id="ui"></div>
        <script src="https://unpkg.com/swagger-ui-dist@5/swagger-ui-bundle.js"></script>
        <script>SwaggerUIBundle({ url: '/openapi.json', dom_id: '#ui' });</script>
        </body></html>
        """;
    public const string OpenApi = """
        {
          "openapi":"3.0.3",
          "info":{"title":"Travel weather GraphQL API","version":"1.0.0"},
          "paths":{"/graphql":{"post":{"summary":"Execute a GraphQL query",
            "requestBody":{"required":true,"content":{"application/json":{
              "schema":{"type":"object","required":["query"],"properties":{"query":{"type":"string"},"variables":{"type":"object"},"operationName":{"type":"string"}}},
              "examples":{
                "locations":{"summary":"List featured locations","value":{"query":"query Locations { locations { name country timezone } }"}},
                "searchLocations":{"summary":"Search any city worldwide","value":{"query":"query Search($name: String!, $count: Int) { searchLocations(name: $name, count: $count) { name country region timezone latitude longitude } }","operationName":"Search","variables":{"name":"Paris","count":5}}},
                "travelWeather":{"summary":"Weather for a location","value":{"query":"query TravelWeather($city: String!, $country: String, $days: Int!) { travelWeather(city: $city, country: $country) { location { name country timezone } current { temperatureC condition windSpeedKmh } forecast(days: $days) { date minTemperatureC maxTemperatureC condition } airQuality { aqi category } alerts { title severity startsAt endsAt } } }","operationName":"TravelWeather","variables":{"city":"London","country":"GB","days":5}}}
              }
            }}},
            "responses":{"200":{"description":"GraphQL result (data and/or errors)"},"400":{"description":"Invalid request"},"413":{"description":"Request body too large"}}
          }}}
        }
        """;
}
