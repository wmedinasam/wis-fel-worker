using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using WisFel.Worker;
using WisFel.Worker.Api;
using WisFel.Worker.Data;
using WisFel.Worker.Sae;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddDbContext<FelDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("FEL")));
builder.Services.AddSingleton<IDocumentSendPolicy, DocumentSendPolicy>();
builder.Services.AddSingleton<ISaeQueryExecutor, SqlSaeQueryExecutor>();
builder.Services.AddScoped<ISaeDocumentReader, SaeDocumentReader>();
// Validated on first use, not at startup: the worker still boots on installations that have no API
// configuration yet.
builder.Services.AddOptions<FelApiOptions>().Bind(builder.Configuration.GetSection("FelApi"));
builder.Services.AddSingleton<IValidateOptions<FelApiOptions>, FelApiOptionsValidator>();
builder.Services.AddHttpClient(FelApiClient.HttpClientName, (serviceProvider, client) =>
    {
        var apiOptions = serviceProvider.GetRequiredService<IOptions<FelApiOptions>>().Value;
        client.BaseAddress = new Uri($"{apiOptions.BaseUrl.TrimEnd('/')}/");
    })
    .SetHandlerLifetime(Timeout.InfiniteTimeSpan)
    .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
    {
        // Reuse the TLS connection between heartbeats instead of renegotiating it every 30 seconds.
        PooledConnectionLifetime = TimeSpan.FromMinutes(5)
    });
builder.Services.AddSingleton<IFelApiClient, FelApiClient>();
builder.Services.AddHostedService<Worker>();

var host = builder.Build();

if (host.Services.GetRequiredService<IHostEnvironment>().IsDevelopment())
{
    var connectionString = builder.Configuration.GetConnectionString("FEL");
    if (string.IsNullOrWhiteSpace(connectionString))
    {
        throw new InvalidOperationException(
            "Missing ConnectionStrings:FEL. Configure it with .NET User Secrets for Development.");
    }

    await using var scope = host.Services.CreateAsyncScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<FelDbContext>();
    await dbContext.Database.MigrateAsync();
}

host.Run();
