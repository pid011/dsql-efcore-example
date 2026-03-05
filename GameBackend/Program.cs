using System.Text.Json.Serialization;
using GameBackend;
using GameBackend.DSQL;
using GameBackend.Options;

var builder = WebApplication.CreateBuilder(args);

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

builder.AddServiceDefaults();
builder.AddDsqlNpgsqlDataSource("gamebackenddb");
builder.Services.Configure<DsqlOptions>(builder.Configuration.GetSection(DsqlOptions.SectionName));
EFCoreApi.ConfigureServices(builder.Services);
builder.Services.AddOpenApi();

var app = builder.Build();

app.MapDefaultEndpoints();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/openapi/v1.json", "GameBackend API");
    });
}

app.UseHttpsRedirection();

EFCoreApi.ConfigureEndpoints(app);

app.Run();