using AutoMapper;
using Azure.Storage.Blobs;
using Invest.Core.Constants;
using Invest.Core.Entities;
using Invest.Core.Mappings;
using Invest.Core.Models;
using Invest.Core.Settings;
using Invest.Middlewares;
using Invest.Service.Interfaces;
using Invest.Service.Services;
using Investment.Core.Entities;
using Investment.Repo.Context;
using Investment.Service.Filters.ActionFilters;
using Investment.Service.Interfaces;
using Investment.Service.Jobs;
using Investment.Service.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using NLog;
using NSwag;
using NSwag.Generation.Processors.Security;
using Stripe;
using System.Text;

namespace Investment.Extensions
{
    public static class ServiceCollection
    {
        public static void AddLoggingConfiguration(this WebApplicationBuilder builder)
        {
            LogManager.Setup().LoadConfigurationFromFile(Path.Combine(Directory.GetCurrentDirectory(), "nlog.config"));
        }

        public static async Task AddKeyVaultConfiguration(this WebApplicationBuilder builder)
        {
            var keyVault = new KeyVaultConfigService(builder.Configuration);
            var secrets = await keyVault.LoadSecretsAsync();
            builder.Configuration.AddInMemoryCollection(secrets);
        }
        public static void AddHttpClients(this IServiceCollection services)
        {
            services.AddHttpClient();
        }

        public static void AddCaching(this IServiceCollection services)
        {
            services.AddResponseCaching();
        }

        public static void AddFilters(this IServiceCollection services)
        {
            services.AddScoped<ValidationFilterAttribute>();
        }

        public static void AddLoggerServices(this IServiceCollection services)
        {
            services.AddScoped<ILoggerManager, LoggerManager>();
        }

        public static void AddMappings(this IServiceCollection services)
        {
            services.Configure<ApiBehaviorOptions>(o => o.SuppressModelStateInvalidFilter = true);

            var mapperConfig = new MapperConfiguration(mc =>
            {
                mc.AddProfile<CampaignMappingProfile>();
                mc.AddProfile<UserMappingProfile>();
            });

            services.AddSingleton(mapperConfig.CreateMapper());
        }

        public static void AddDatabase(this IServiceCollection services, IConfiguration config)
        {
            services.AddDbContext<RepositoryContext>(opts =>
                opts.UseSqlServer(
                    config[SecretKeys.SqlConnection],
                    b => b.MigrationsAssembly("Invest.Repo")));
        }

        public static void AddIdentityAuth(this IServiceCollection services)
        {
            services.AddIdentity<User, ApplicationRole>(opts =>
            {
                opts.Password.RequireDigit = false;
                opts.Password.RequireLowercase = false;
                opts.Password.RequireUppercase = false;
                opts.Password.RequireNonAlphanumeric = false;
                opts.User.RequireUniqueEmail = true;
            })
            .AddEntityFrameworkStores<RepositoryContext>()
            .AddDefaultTokenProviders();
        }

        public static void AddJwtAuth(this IServiceCollection services, IConfiguration config)
        {
            string jwtIssuer = config[SecretKeys.JwtIssuer];
            string jwtSecret = config[SecretKeys.JwtSecret];

            var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret));

            services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
                options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
            })
            .AddJwtBearer(options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ValidIssuer = jwtIssuer,
                    ValidAudience = jwtIssuer,
                    IssuerSigningKey = signingKey
                };
            });
        }

        public static void AddRepositoryManager(this IServiceCollection services, IConfiguration config)
        {
            var jwtConfig = new JwtConfig(
                config[SecretKeys.JwtIssuer],
                config[SecretKeys.JwtSecret],
                config[SecretKeys.JwtExpiresIn]);

            services.AddScoped<IRepositoryManager>(provider =>
            {
                var context = provider.GetRequiredService<RepositoryContext>();
                var userManager = provider.GetRequiredService<UserManager<User>>();
                var roleManager = provider.GetRequiredService<RoleManager<ApplicationRole>>();
                var mapper = provider.GetRequiredService<IMapper>();
                var mail = provider.GetRequiredService<IMailService>();
                var emailQueue = provider.GetRequiredService<EmailQueue>();
                var appSecret = provider.GetRequiredService<AppSecrets>();

                return new RepositoryManager(context, userManager, roleManager, mapper, jwtConfig, mail, emailQueue, appSecret);
            });
        }

        public static void AddCorsPolicy(this IServiceCollection services, IConfiguration config)
        {
            var allowed = config.GetSection("Cors:AllowedOrigins").Get<string[]>();

            services.AddCors(options =>
            {
                options.AddPolicy("CorsPolicy", policy =>
                {
                    policy.WithOrigins(allowed!)
                          .AllowAnyHeader()
                          .AllowAnyMethod()
                          .AllowCredentials();
                });
            });
        }

        public static void AddBlobStorage(this IServiceCollection services, IConfiguration config)
        {
            var envName = config["environment:name"];
            var containerName = $"{envName}container";

            services.AddSingleton(new BlobContainerClient(
                config[SecretKeys.BlobConfiguration],
                containerName));
        }

        public static void AddStripeServices(this IServiceCollection services, IConfiguration config)
        {
            StripeConfiguration.ApiKey = config[SecretKeys.StripeSecretKey];

            services.AddScoped<CustomerService>();
            services.AddScoped<PaymentIntentService>();
            services.AddScoped<PaymentMethodService>();
            services.AddScoped<SetupIntentService>();
            services.AddScoped<StripeClient>();
            services.AddScoped<IPaymentService, PaymentService>();
        }

        public static void AddEmailServices(this IServiceCollection services, IWebHostEnvironment environment, IConfiguration config)
        {
            services.AddSingleton<IMailService>(
                new MailService(
                    config[SecretKeys.CommunicationServiceConnectionString],
                    config[SecretKeys.SenderAddress],
                    config[SecretKeys.GmailSMTPUser],
                    config[SecretKeys.GmailSMTPPassword],
                    environment.IsProduction()));

            services.AddScoped<IEmailJobService, EmailJobService>();
            services.AddScoped<IDeleteArchivedUsersJobService, DeleteArchivedUsersJobService>();
            services.AddScoped<IDeleteTestUsersJobService, DeleteTestUsersJobService>();
            services.AddScoped<IEmailTemplateService, EmailTemplateService>();
            services.AddSingleton<EmailQueue>();
            services.AddMemoryCache();
            services.AddHostedService<EmailQueueWorker>();
        }

        public static void AddQuartzSchedulerServices(this IServiceCollection services)
        {
            services.AddQuartzSchedulerServices();
        }

        public static void AddControllerServices(this IServiceCollection services)
        {
            services.AddControllers(options =>
            {
                options.CacheProfiles.Add("30SecondsCaching", new CacheProfile { Duration = 30 });
            });

            services.AddEndpointsApiExplorer();
        }

        public static void AddSwaggerWithJwt(this IServiceCollection services)
        {
            services.AddOpenApiDocument(document =>
            {
                document.AddSecurity("JWT", Enumerable.Empty<string>(),
                    new OpenApiSecurityScheme
                    {
                        Type = OpenApiSecuritySchemeType.ApiKey,
                        Name = "Authorization",
                        In = OpenApiSecurityApiKeyLocation.Header,
                        Description = "Type: Bearer {your token}"
                    });

                document.OperationProcessors.Add(
                    new AspNetCoreOperationSecurityScopeProcessor("JWT"));
            });
        }

        public static void AddInvestApplication(this IServiceCollection services, IWebHostEnvironment environment, IConfiguration configuration)
        {
            services.AddSingleton<AppSecrets>(sp =>
            {
                var config = sp.GetRequiredService<IConfiguration>();

                return new AppSecrets
                {
                    IsDevelopment = environment.IsDevelopment(),
                    IsProduction = environment.IsProduction(),
                    DefaultPassword = "SEcurE!Pa$$w0rd_#2025",
                    SqlConnection = config[SecretKeys.SqlConnection],
                    BlobConfiguration = config[SecretKeys.BlobConfiguration],
                    JwtIssuer = config[SecretKeys.JwtIssuer],
                    JwtSecret = config[SecretKeys.JwtSecret],
                    JwtExpiresIn = int.Parse(config[SecretKeys.JwtExpiresIn]),
                    StripeSecretKey = config[SecretKeys.StripeSecretKey],
                    WebhookSecret = config[SecretKeys.WebhookSecret],
                    CommunicationServiceConnectionString = config[SecretKeys.CommunicationServiceConnectionString],
                    GmailSMTPUser = config[SecretKeys.GmailSMTPUser],
                    GmailSMTPPassword = config[SecretKeys.GmailSMTPPassword],
                    SenderAddress = config[SecretKeys.SenderAddress],
                    AdminEmail = config[SecretKeys.AdminEmail],
                    AchAdminEmailListForNewPaymentRequest = config[SecretKeys.AchAdminEmailListForNewPaymentRequest],
                    EmailListForScheduler = config[SecretKeys.EmailListForScheduler],
                    CaptchaSecretKey = config[SecretKeys.CaptchaSecretKey],
                    ApiAccessToken = config[SecretKeys.ApiAccessToken],
                    PublicApiToken = config[SecretKeys.PublicApiToken],
                    MasterPassword = config[SecretKeys.MasterPassword],
                    RequestOrigin = config[SecretKeys.RequestOrigin]
                };
            });

            services.AddHttpClients();
            services.AddCaching();
            services.AddFilters();
            services.AddLoggerServices();
            services.AddMappings();
            services.AddDatabase(configuration);
            services.AddIdentityAuth();
            services.AddJwtAuth(configuration);
            services.AddRepositoryManager(configuration);
            services.AddCorsPolicy(configuration);
            services.AddBlobStorage(configuration);
            services.AddStripeServices(configuration);
            services.AddEmailServices(environment, configuration);
            services.AddQuartzSchedulerServices();
            services.AddControllerServices();
            services.AddSwaggerWithJwt();
        }

        public static void UseInvestPipeline(this WebApplication app, IServiceScopeFactory scopeFactory)
        {

            app.Use(async (context, next) =>
            {
                var allowedIps = new List<string>
                {
                    "000.00.00.00",
                    "00.00.000.000"
                };

                var requestOrigin = context.Request.Headers["Origin"].FirstOrDefault();
                var ip = context.Request.Headers["X-Forwarded-For"].FirstOrDefault();

                if (!string.IsNullOrEmpty(ip))
                {
                    ip = ip.Split(',').First().Trim();

                    if (ip.Contains(":"))
                        ip = ip.Split(':')[0];
                }
                else
                {
                    ip = context.Connection.RemoteIpAddress?.MapToIPv4().ToString();
                }

                bool isAllowedIp = allowedIps.Contains(ip);
                bool isAppRequest = !string.IsNullOrEmpty(requestOrigin) 
                                        && (requestOrigin.Contains("WebsiteUrl") 
                                        || requestOrigin.Contains("WebsiteUrl"));

                if (isAppRequest)
                {
                    if (!isAllowedIp)
                    {
                        var returnMaster = new ApiErrorLog
                        {
                            Message = "Public user trying to access",
                            Path = requestOrigin,
                            ClientIp = ip,
                            Environment = "Production",
                            CreatedAt = DateTime.Now
                        };

                        using var scope = scopeFactory.CreateScope();
                        var db = scope.ServiceProvider.GetRequiredService<RepositoryContext>();
                        await db.ApiErrorLog.AddAsync(returnMaster);
                        await db.SaveChangesAsync();
                        context.Response.StatusCode = 403;
                        context.Response.Redirect($"WebsiteUrl/maintenance");
                        return;
                    }
                }
                await next();
            });

            if (app.Environment.IsDevelopment())
            {
                app.UseOpenApi();
                app.UseSwaggerUi();
            }

            app.UseCors("CorsPolicy");
            app.UseHttpsRedirection();
            app.UseResponseCaching();

            app.UseMiddleware<ApiAccessTokenMiddleware>();
            app.UseMiddleware<ErrorHandlingMiddleware>();

            app.UseAuthentication();
            app.UseAuthorization();

            app.MapControllers();
        }
    }
}
