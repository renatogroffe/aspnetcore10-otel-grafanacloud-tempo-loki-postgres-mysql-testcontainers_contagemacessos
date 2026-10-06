using APIContagem;
using APIContagem.Data;
using APIContagem.Models;
using APIContagem.Tracing;
using APIContagem.Utils;
using DotNet.Testcontainers.Builders;
using Grafana.OpenTelemetry;
using Microsoft.EntityFrameworkCore;
using OpenTelemetry.Exporter;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Scalar.AspNetCore;
using Testcontainers.MySql;
using Testcontainers.PostgreSql;

var builder = WebApplication.CreateBuilder(args);

var grafanaCloudZone = builder.Configuration["GrafanaCloud:Zone"];
var grafanaCloudInstanceId = builder.Configuration["GrafanaCloud:InstanceId"];
var grafanaCloudApiKey = builder.Configuration["GrafanaCloud:ApiKey"];

if (string.IsNullOrWhiteSpace(grafanaCloudZone) ||
    string.IsNullOrWhiteSpace(grafanaCloudInstanceId) ||
    string.IsNullOrWhiteSpace(grafanaCloudApiKey))
{
    throw new InvalidOperationException(
        "Configure GrafanaCloud:Zone, GrafanaCloud:InstanceId e GrafanaCloud:ApiKey por User Secrets ou variaveis de ambiente.");
}

var grafanaCloudAuthorization = Convert.ToBase64String(
    System.Text.Encoding.UTF8.GetBytes($"{grafanaCloudInstanceId}:{grafanaCloudApiKey}"));

Console.WriteLine("Criando container para uso do PostgreSQL...");
var postgresContainer = new PostgreSqlBuilder("postgres:17.6")
    .WithResourceMapping(
        DBFileAsByteArray.GetContent("BaseContagemPostgreSql.sql"),
        "/docker-entrypoint-initdb.d/01-init.sql")
    .WithWaitStrategy(Wait.ForUnixContainer().UntilCommandIsCompleted("pg_isready"))
    .Build();
await postgresContainer.StartAsync();
var postgresConnectionString = postgresContainer.GetConnectionString()
    .Replace(";Database=postgres;", ";Database=basecontagem;");

builder.Services.AddDbContext<ContagemPostgresContext>(options =>
{
    options.UseNpgsql(postgresConnectionString, o => o.UseNodaTime());
});

Console.WriteLine("Criando container para uso do MySQL...");
var mysqlContainer = new MySqlBuilder("mysql:8.4")
    .WithDatabase("basecontagem")
    .WithUsername("root")
    .WithPassword("root")
    .WithResourceMapping(
        DBFileAsByteArray.GetContent("BaseContagemMySql.sql"),
        "/docker-entrypoint-initdb.d/01-init.sql")
    .WithWaitStrategy(Wait.ForUnixContainer()
        .UntilCommandIsCompleted("mysqladmin ping -h localhost -uroot -proot --silent"))
    .Build();
await mysqlContainer.StartAsync();
var mysqlConnectionString = mysqlContainer.GetConnectionString();

builder.Services.AddDbContext<ContagemMySqlContext>(options =>
{
    options.UseMySQL(mysqlConnectionString);
});

builder.Services.AddScoped<ContagemRepository>();
builder.Services.AddScoped<ContagemMySqlRepository>();

var resourceBuilder = ResourceBuilder.CreateDefault()
    .AddService(serviceName: OpenTelemetryExtensions.ServiceName,
        serviceVersion: OpenTelemetryExtensions.ServiceVersion);
builder.Services.AddOpenTelemetry()
    .WithTracing((traceBuilder) =>
    {
        traceBuilder
            .AddSource(OpenTelemetryExtensions.ServiceName)
            .SetResourceBuilder(resourceBuilder)
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation()
            .AddEntityFrameworkCoreInstrumentation()
            .AddConsoleExporter()
            .UseGrafana(settings =>
            {
                settings.ExporterSettings = new OtlpExporter
                {
                    Endpoint = new Uri($"https://otlp-gateway-prod-{grafanaCloudZone}.grafana.net/otlp"),
                    Headers = $"Authorization=Basic {grafanaCloudAuthorization}",
                    Protocol = OtlpExportProtocol.HttpProtobuf
                };
            });
    });
builder.Logging.AddOpenTelemetry(options =>
{
    options.SetResourceBuilder(resourceBuilder);
    options.IncludeFormattedMessage = true;
    options.IncludeScopes = true;
    options.ParseStateValues = true;
    options.AttachLogsToActivityEvent();
    options.UseGrafana(settings =>
    {
        settings.ExporterSettings = new OtlpExporter
        {
            Endpoint = new Uri($"https://otlp-gateway-prod-{grafanaCloudZone}.grafana.net/otlp"),
            Headers = $"Authorization=Basic {grafanaCloudAuthorization}",
            Protocol = OtlpExportProtocol.HttpProtobuf
        };
    });
});

builder.Services.AddOpenApi();
builder.Services.AddCors();

builder.Services.AddSingleton<Contador>();

var app = builder.Build();

app.MapOpenApi();
app.MapScalarApiReference(options =>
{
    options.Title = "API de Contagem de Acessos";
    options.Theme = ScalarTheme.BluePlanet;
    options.DarkMode = true;
});

Lock ContagemLock = new();

static ResultadoContador CriarResultado(string tecnologiaBD, Contador contador, IConfiguration configuration)
{
    return new ResultadoContador()
    {
        ValorAtual = contador.ValorAtual,
        Local = contador.Local,
        Kernel = contador.Kernel,
        Framework = contador.Framework,
        Mensagem = tecnologiaBD + " --> " + configuration["Saudacao"]
    };
}

app.MapGet("/contador/postgres", async (ContagemRepository repository, Contador contador, IConfiguration configuration) =>
{
    using var activity1 = OpenTelemetryExtensions.ActivitySource
        .StartActivity("GerarValorContagem_Postgres")!;

    int valorAtualContador;
    using (ContagemLock.EnterScope())
    {
        contador.Incrementar();
        valorAtualContador = contador.ValorAtual;
    }

    activity1.SetTag("valorAtual", valorAtualContador);
    app.Logger.LogInformation($"Conteúdo Postgres - Valor atual: {valorAtualContador}");

    var resultadoContador = CriarResultado("Postgres", contador, configuration);
    activity1.Stop();

    using var activity2 = OpenTelemetryExtensions.ActivitySource
        .StartActivity("RegistrarRetornarValorContagem_Postgres")!;

    repository.Insert(resultadoContador);
    app.Logger.LogInformation($"Registro PostgreSQL inserido com sucesso! Valor: {valorAtualContador}");

    activity2.SetTag("valorAtual", valorAtualContador);
    activity2.SetTag("horario", $"{DateTime.UtcNow.AddHours(-3):HH:mm:ss}");

    return Results.Ok(resultadoContador);
})
.Produces<ResultadoContador>();

app.MapGet("/contador/mysql", async (ContagemMySqlRepository repository, Contador contador, IConfiguration configuration) =>
{
    using var activity1 = OpenTelemetryExtensions.ActivitySource
        .StartActivity("GerarValorContagem_MySQL")!;

    int valorAtualContador;
    using (ContagemLock.EnterScope())
    {
        contador.Incrementar();
        valorAtualContador = contador.ValorAtual;
    }

    activity1.SetTag("valorAtual", valorAtualContador);
    app.Logger.LogInformation($"Conteúdo MySQL - Valor atual: {valorAtualContador}");

    var resultadoContador = CriarResultado("MySQL", contador, configuration);
    activity1.Stop();

    using var activity2 = OpenTelemetryExtensions.ActivitySource
        .StartActivity("RegistrarRetornarValorContagem_MySQL")!;

    repository.Insert(resultadoContador);
    app.Logger.LogInformation($"Registro MySQL inserido com sucesso! Valor: {valorAtualContador}");

    activity2.SetTag("valorAtual", valorAtualContador);
    activity2.SetTag("horario", $"{DateTime.UtcNow.AddHours(-3):HH:mm:ss}");

    return Results.Ok(resultadoContador);
})
.Produces<ResultadoContador>();

app.MapGet("/connectionstring/postgres", () =>
{
    using var activity = OpenTelemetryExtensions.ActivitySource
        .StartActivity("ObterConnectionString_Postgres")!;

    app.Logger.LogInformation($"Connection string do PostgreSQL: {postgresConnectionString}");
    return Results.Text(postgresConnectionString, "application/text");
});

app.MapGet("/connectionstring/mysql", () =>
{
    using var activity = OpenTelemetryExtensions.ActivitySource
        .StartActivity("ObterConnectionString_MySQL")!;

    app.Logger.LogInformation($"Connection string do MySQL: {mysqlConnectionString}");
    return Results.Text(mysqlConnectionString, "application/text");
});

app.MapGet("/badrequest", () =>
{
    using var activity = OpenTelemetryExtensions.ActivitySource
        .StartActivity("SimularBadRequest")!;

    activity.SetTag("erro", "Simulação de Bad Request");
    app.Logger.LogWarning("Simulação de Bad Request realizada.");

    return Results.BadRequest(new { Erro = "Este é um Bad Request simulado." });
});

app.MapGet("/error", () =>
{
    using var activity = OpenTelemetryExtensions.ActivitySource
        .StartActivity("SimularErroInterno")!;

    activity.SetTag("erro", "Simulação de erro interno");
    app.Logger.LogError("Simulação de erro interno (500).");

    throw new InvalidOperationException("Erro simulado para teste de métricas");
});

app.Run();
