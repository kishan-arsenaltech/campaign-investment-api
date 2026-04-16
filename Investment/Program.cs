using Investment.Extensions;
using NLog.Web;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseNLog();

builder.AddLoggingConfiguration();

await builder.AddKeyVaultConfiguration();

builder.Services.AddInvestApplication(builder.Environment, builder.Configuration);

var app = builder.Build();

var scopeFactory = app.Services.GetRequiredService<IServiceScopeFactory>();

app.UseInvestPipeline(scopeFactory);

app.Run();