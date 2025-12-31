using AutoMapper;
using Azure.Storage.Blobs;
using Invest.Core.Mappings;
using Invest.Service.Interfaces;
using Investment.Core.Entities;
using Investment.Repo.Context;
using Investment.Service.Filters.ActionFilters;
using Investment.Service.Interfaces;
using Investment.Service.Scheduler;
using Investment.Service.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Stripe;
using System.Text;

namespace Investment.Extensions
{
    public static class ServiceCollection
    {
        public static void ConfigureLoggerService(this IServiceCollection services)
            => services.AddScoped<ILoggerManager, LoggerManager>();

        public static void ConfigureSqlContext(this IServiceCollection services, string sqlConnection)
            => services.AddDbContext<RepositoryContext>(opts => opts.UseSqlServer(sqlConnection, b => b.MigrationsAssembly("Investment.Repo")));

        public static void ConfigureRepositoryManager(this IServiceCollection services, JwtConfig jwtConfig)
        {
            services.AddScoped<IRepositoryManager>(provider =>
            {
                var jwtConfigName = jwtConfig.JwtConfigName;
                var jwtSecret = jwtConfig.JwtSecret;
                var jwtExpiresIn = jwtConfig.JwtExpiresIn;

                var userManager = provider.GetRequiredService<UserManager<User>>();
                var roleManager = provider.GetRequiredService<RoleManager<IdentityRole>>();
                var mapper = provider.GetRequiredService<IMapper>();
                var mailService = provider.GetRequiredService<IMailService>();

                var repositoryContext = provider.GetRequiredService<RepositoryContext>();

                return new RepositoryManager(repositoryContext, userManager, roleManager, mapper, jwtConfig, mailService);
            });
        }

        public static void ConfigureMapping(this IServiceCollection services)
        {
            services.Configure<ApiBehaviorOptions>(options =>
            {
                options.SuppressModelStateInvalidFilter = true;
            });

            var mapperConfig = new MapperConfiguration(cfg =>
            {
                cfg.AddProfile<CampaignMappingProfile>();
                cfg.AddProfile<UserMappingProfile>();
            });

            services.AddSingleton(mapperConfig.CreateMapper());
        }

        public static void ConfigureControllers(this IServiceCollection services)
        {
            services.AddControllers(config =>
            {
                config.CacheProfiles.Add("30SecondsCaching", new CacheProfile
                {
                    Duration = 30
                });
            });
        }

        public static void ConfigureResponseCaching(this IServiceCollection services) => services.AddResponseCaching();

        public static void ConfigureIdentity(this IServiceCollection services)
        {
            var builder = services.AddIdentity<User, IdentityRole>(options =>
            {
                options.Password.RequireDigit = false;
                options.Password.RequireLowercase = false;
                options.Password.RequireUppercase = false;
                options.Password.RequireNonAlphanumeric = false;
                options.User.RequireUniqueEmail = true;
            })
            .AddEntityFrameworkStores<RepositoryContext>()
            .AddDefaultTokenProviders();
        }

        public static void ConfigureJWT(this IServiceCollection services, string secretJwtNameValue, string secretJwtSecreValue)
        {
            services.AddAuthentication(opt =>
            {
                opt.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
                opt.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
            })
            .AddJwtBearer(options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ValidIssuer = secretJwtNameValue,
                    ValidAudience = secretJwtNameValue,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretJwtSecreValue))
                }; ;
            });
        }

        public static void ConfigureSwagger(this IServiceCollection services)
        {
            services.AddSwaggerGen(c =>
            {
                c.SwaggerDoc("v1", new OpenApiInfo
                {
                    Title = "Investment Campaign API",
                    Version = "v1",
                    Description = "Investment Campaign API Services."
                });
                c.ResolveConflictingActions(apiDescriptions => apiDescriptions.First());
                c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
                {
                    Name = "Authorization",
                    Type = SecuritySchemeType.ApiKey,
                    Scheme = "Bearer",
                    BearerFormat = "JWT",
                    In = ParameterLocation.Header,
                    Description = "JWT Authorization header using the Bearer scheme."
                });

                c.AddSecurityRequirement(new OpenApiSecurityRequirement
                {
                    {
                        new OpenApiSecurityScheme
                        {
                            Reference = new OpenApiReference
                            {
                                Type = ReferenceType.SecurityScheme,
                                Id = "Bearer"
                            }
                        },
                        new string[] {}
                    }
                });
            });
        }

        public static void RegisterDependencies(this IServiceCollection services)
        {
            services.AddScoped<ValidationFilterAttribute>();
        }

        public static void AddApplicationServices(this IServiceCollection services, KeyVaultConfigService keyVaultService, string environmentName)
        {
            string blobContainerName = $"{environmentName}container";
            services.AddSingleton(new BlobContainerClient(keyVaultService.GetBlobConfiguration(), blobContainerName));
            services.AddSingleton<IHttpContextAccessor, HttpContextAccessor>();

            StripeConfiguration.ApiKey = keyVaultService.GetStripeSecretKey();
            services.AddScoped<CustomerService>();
            services.AddScoped<PaymentIntentService>();
            services.AddScoped<PaymentMethodService>();
            services.AddScoped<SetupIntentService>();
            services.AddScoped<PaymentIntent>();
            services.AddScoped<StripeClient>();
            services.AddScoped<IPaymentService, PaymentService>();

            // Mail Services
            services.AddSingleton<IMailService>(new MailService(
                keyVaultService.GetCommunicationServiceConnectionString(),
                keyVaultService.GetSenderAddress()
            ));
            services.AddScoped<IEmailJobService, EmailJobService>();

            // Quartz Scheduler
            services.AddQuartzScheduler();
        }
    }
}
