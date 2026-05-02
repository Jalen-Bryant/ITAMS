using System.IdentityModel.Tokens.Jwt;
using System.Text;
using ITAMS.Api.Authorization;
using ITAMS.Api.Configuration;
using ITAMS.Api.Endpoints;
using ITAMS.Api.ErrorHandling;
using ITAMS.Api.Models;
using ITAMS.Api.Services;
using ITAMS.Api.Swagger;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using MongoDB.Driver;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddOpenApi();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    var bearerScheme = new OpenApiSecurityScheme
    {
        Description = "JWT Bearer token",
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT"
    };

    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "ITAMS API",
        Version = "v1",
        Description = "MongoDB-backed API for IT asset management."
    });

    options.AddSecurityDefinition("Bearer", bearerScheme);

    options.AddSecurityRequirement(document => new OpenApiSecurityRequirement
    {
        [new OpenApiSecuritySchemeReference("Bearer", document, null)] = []
    });
    options.OperationFilter<BearerSecurityOperationFilter>();
});

builder.Services
    .AddOptions<MongoDbSettings>()
    .Bind(builder.Configuration.GetSection(MongoDbSettings.SectionName))
    .ValidateDataAnnotations()
    .Validate(
        settings => !string.IsNullOrWhiteSpace(settings.ConnectionString),
        "MongoDb:ConnectionString is required.")
    .ValidateOnStart();

builder.Services
    .AddOptions<JwtSettings>()
    .Bind(builder.Configuration.GetSection(JwtSettings.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services
    .AddOptions<BootstrapAdminSettings>()
    .Bind(builder.Configuration.GetSection(BootstrapAdminSettings.SectionName));

builder.Services
    .AddOptions<CorsSettings>()
    .Bind(builder.Configuration.GetSection(CorsSettings.SectionName));

var configuredCorsSettings = builder.Configuration.GetSection(CorsSettings.SectionName).Get<CorsSettings>() ?? new CorsSettings();
// Normalize configured origins once so the runtime policy does not have to reason about whitespace or trailing slashes.
var allowedOrigins = configuredCorsSettings.AllowedOrigins
    .Where(origin => !string.IsNullOrWhiteSpace(origin))
    .Select(origin => origin.Trim().TrimEnd('/'))
    .Distinct(StringComparer.OrdinalIgnoreCase)
    .ToArray();

builder.Services.AddCors(options =>
{
    options.AddPolicy(CorsSettings.PolicyName, policy =>
    {
        if (allowedOrigins.Length == 0)
        {
            return;
        }

        policy.WithOrigins(allowedOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer();

builder.Services
    .AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<IOptions<JwtSettings>>((options, jwtOptions) =>
    {
        var configuredJwtSettings = jwtOptions.Value;

        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = configuredJwtSettings.Issuer,
            ValidateAudience = true,
            ValidAudience = configuredJwtSettings.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(configuredJwtSettings.SigningKey)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(1),
            NameClaimType = JwtRegisteredClaimNames.UniqueName,
            RoleClaimType = "role"
        };
        options.Events = new JwtBearerEvents
        {
            OnTokenValidated = async context =>
            {
                // Access tokens stay stateless, but every request still checks that the backing refresh session remains active.
                var sessionValidationService = context.HttpContext.RequestServices.GetRequiredService<SessionValidationService>();
                var isActive = await sessionValidationService.IsPrincipalActiveAsync(
                    context.Principal!,
                    context.HttpContext.RequestAborted);
                if (!isActive)
                {
                    context.Fail("The current session is no longer valid.");
                }
            }
        };
    });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(AuthorizationPolicies.Authenticated, policy => policy.RequireAuthenticatedUser());
    options.AddPolicy(AuthorizationPolicies.UserRead, policy => policy.RequireRole(AuthorizationPolicies.UserReadRoles));
    options.AddPolicy(AuthorizationPolicies.UserWrite, policy => policy.RequireRole(AuthorizationPolicies.UserWriteRoles));
    options.AddPolicy(AuthorizationPolicies.AssetRead, policy => policy.RequireRole(AuthorizationPolicies.AssetReadRoles));
    options.AddPolicy(AuthorizationPolicies.AssetWrite, policy => policy.RequireRole(AuthorizationPolicies.AssetWriteRoles));
    options.AddPolicy(AuthorizationPolicies.AssignmentRead, policy => policy.RequireRole(AuthorizationPolicies.AssignmentReadRoles));
    options.AddPolicy(AuthorizationPolicies.AssignmentWrite, policy => policy.RequireRole(AuthorizationPolicies.AssignmentWriteRoles));
    options.AddPolicy(AuthorizationPolicies.HistoryRead, policy => policy.RequireRole(AuthorizationPolicies.HistoryReadRoles));
    options.AddPolicy(AuthorizationPolicies.ReportsRead, policy => policy.RequireRole(AuthorizationPolicies.ReportsReadRoles));
});

builder.Services.AddSingleton<IMongoClient>(serviceProvider =>
{
    var settings = serviceProvider.GetRequiredService<IOptions<MongoDbSettings>>().Value;
    return new MongoClient(settings.ConnectionString);
});
builder.Services.AddSingleton<IPasswordHasher<UserDocument>, PasswordHasher<UserDocument>>();
builder.Services.AddSingleton<AssetsService>();
builder.Services.AddSingleton<AssetMutationService>();
builder.Services.AddSingleton<AssignmentMutationService>();
builder.Services.AddSingleton<AssignmentsService>();
builder.Services.AddSingleton<AuditLogsService>();
builder.Services.AddSingleton<AuthService>();
builder.Services.AddSingleton<BootstrapAdminService>();
builder.Services.AddSingleton<CurrentUserService>();
builder.Services.AddSingleton<LifecycleEventsService>();
builder.Services.AddSingleton<OperationContextService>();
builder.Services.AddSingleton<ReferenceIntegrityService>();
builder.Services.AddSingleton<ReportsService>();
builder.Services.AddSingleton<SessionValidationService>();
builder.Services.AddSingleton<TokenService>();
builder.Services.AddSingleton<UsersService>();
builder.Services.AddSingleton<UserMutationService>();
builder.Services.AddSingleton<UserSessionsService>();

var app = builder.Build();

app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "ITAMS API v1");
        options.RoutePrefix = "swagger";
        options.ConfigObject.PersistAuthorization = true;
    });
    app.MapOpenApi();
}

app.UseHttpsRedirection();
app.UseCors(CorsSettings.PolicyName);
app.UseAuthentication();
app.UseAuthorization();
app.MapAuthEndpoints();
app.MapAssetEndpoints();
app.MapAssignmentEndpoints();
app.MapAuditLogEndpoints();
app.MapLifecycleEventEndpoints();
app.MapReportsEndpoints();
app.MapUserEndpoints();

// These startup tasks keep Mongo indexes and the first login path in place before the app begins serving requests.
await app.Services.GetRequiredService<UsersService>().EnsureAuthIndexesAsync();
await app.Services.GetRequiredService<UserSessionsService>().EnsureIndexesAsync();
await app.Services.GetRequiredService<AssetsService>().EnsureIndexesAsync();
await app.Services.GetRequiredService<BootstrapAdminService>().EnsureBootstrapAdminAsync();

app.Run();

public partial class Program;
