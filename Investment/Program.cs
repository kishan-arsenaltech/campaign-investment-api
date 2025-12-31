using Invest.Middlewares;
using Investment.Core.Entities;
using Investment.Extensions;
using NLog;

var builder = WebApplication.CreateBuilder(args);

// Logger
var logger = LogManager.Setup().LoadConfigurationFromFile("nlog.config").GetCurrentClassLogger();
builder.Services.ConfigureLoggerService();


// Key Vault
var keyVaultService = new KeyVaultConfigService();
//keyVaultService.InitializeAsync().GetAwaiter().GetResult();
builder.Services.AddSingleton(keyVaultService);


// Secrets
var sqlConnection = keyVaultService.GetSqlConnectionString();
var jwtConfig = new JwtConfig(
    keyVaultService.GetJwtConfigName(),
    keyVaultService.GetJwtSecret(),
    keyVaultService.GetJwtExpiresIn()
);
var environmentName = builder.Configuration.GetValue<string>("environment:name");


// Core services
builder.Services.AddHttpClient();
builder.Services.ConfigureResponseCaching();
builder.Services.RegisterDependencies();
//builder.Services.ConfigureMapping();
builder.Services.ConfigureSqlContext(sqlConnection);
builder.Services.ConfigureRepositoryManager(jwtConfig);
builder.Services.ConfigureIdentity();
builder.Services.ConfigureJWT(jwtConfig.JwtConfigName, jwtConfig.JwtSecret);
builder.Services.ConfigureControllers();
builder.Services.AddCors();
builder.Services.ConfigureSwagger();


// Application-specific services
builder.Services.AddApplicationServices(keyVaultService, environmentName!);


var app = builder.Build();


// Middleware
if (app.Environment.IsDevelopment())
{
    app.UseOpenApi();
    app.UseSwaggerUI();
}

app.UseCors(policy => policy
    .AllowAnyMethod()
    .AllowAnyHeader()
    .SetIsOriginAllowed(origin => true)
    .AllowCredentials());

app.UseHttpsRedirection();
app.UseResponseCaching();

app.UseMiddleware<ApiAccessTokenMiddleware>();
app.UseMiddleware<ErrorHandlingMiddleware>();

app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.Run();
