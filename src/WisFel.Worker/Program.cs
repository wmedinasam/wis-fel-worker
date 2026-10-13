using Microsoft.EntityFrameworkCore;
using WisFel.Worker;
using WisFel.Worker.Data;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddDbContext<FelDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("FEL")));
builder.Services.AddSingleton<IDocumentSendPolicy, DocumentSendPolicy>();
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
