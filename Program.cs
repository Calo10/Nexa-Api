using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using NexaApi.Data;
using NexaApi.Services;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();

// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "NexaApi",
        Version = "v1",
        Description = "Nexa API - Authentication and Organization Management"
    });

    // Add JWT Authentication to Swagger
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "JWT Authorization header using the Bearer scheme. Enter ONLY your token (without 'Bearer' prefix) in the text input below.\n\nExample: 12345abcdef",
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.Http,
        Scheme = "Bearer",
        BearerFormat = "JWT"
    });

    options.AddSecurityRequirement(new OpenApiSecurityRequirement
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
            Array.Empty<string>()
        }
    });
});

// Register data access
builder.Services.AddSingleton<IDbConnectionFactory, DbConnectionFactory>();
builder.Services.AddScoped<NexaApi.Data.IAuthRepository, NexaApi.Data.AuthRepository>();
builder.Services.AddScoped<NexaApi.Data.IOrgRepository, NexaApi.Data.OrgRepository>();
builder.Services.AddScoped<NexaApi.Data.IFeaturesRepository, NexaApi.Data.FeaturesRepository>();
builder.Services.AddScoped<NexaApi.Data.BillingRepository>();

// Register services
builder.Services.AddScoped<IJwtTokenService, JwtTokenService>();
builder.Services.AddHttpClient<IEmailService, EmailService>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IOrganizationsService, OrganizationsService>();
builder.Services.AddScoped<IMembersService, MembersService>();
builder.Services.AddScoped<IInvitesService, InvitesService>();
builder.Services.AddScoped<IBillingService, BillingService>();
builder.Services.AddScoped<IFeaturesService, FeaturesService>();

// Configure JWT Authentication
var jwtSettings = builder.Configuration.GetSection("JwtSettings");
var secretKey = jwtSettings["SecretKey"] ?? throw new InvalidOperationException("JWT SecretKey is not configured");
var issuer = jwtSettings["Issuer"] ?? "NexaApi";
var audience = jwtSettings["Audience"] ?? "NexaApi";

// Log JWT configuration (without exposing the full secret key)
var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
var configLogger = loggerFactory.CreateLogger<Program>();
configLogger.LogInformation("JWT Configuration - Issuer: {Issuer}, Audience: {Audience}, KeyLength: {KeyLength}", 
    issuer, audience, secretKey.Length);

builder.Services.AddAuthentication(options =>
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
        ValidIssuer = issuer,
        ValidAudience = audience,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey)),
        ClockSkew = TimeSpan.FromMinutes(5) // Allow 5 minutes clock skew
    };
    
    // Add event handlers for debugging
    options.Events = new JwtBearerEvents
    {
        OnAuthenticationFailed = context =>
        {
            var logger = context.HttpContext.RequestServices.GetRequiredService<ILogger<Program>>();
            var exception = context.Exception;
            
            logger.LogError(exception, "JWT Authentication failed. Exception type: {ExceptionType}", exception.GetType().Name);
            
            if (exception is SecurityTokenExpiredException)
            {
                logger.LogError("Token has expired");
            }
            else if (exception is SecurityTokenInvalidSignatureException)
            {
                logger.LogError("Token signature is invalid");
            }
            else if (exception is SecurityTokenInvalidIssuerException)
            {
                logger.LogError("Token issuer is invalid. Expected: {ExpectedIssuer}, Got: {ActualIssuer}", 
                    jwtSettings["Issuer"], exception.Message);
            }
            else if (exception is SecurityTokenInvalidAudienceException)
            {
                logger.LogError("Token audience is invalid. Expected: {ExpectedAudience}", jwtSettings["Audience"]);
            }
            else
            {
                logger.LogError("Token validation failed: {Message}", exception.Message);
            }
            
            return Task.CompletedTask;
        },
        OnTokenValidated = context =>
        {
            var logger = context.HttpContext.RequestServices.GetRequiredService<ILogger<Program>>();
            var userId = context.Principal?.FindFirst("sub")?.Value;
            var orgId = context.Principal?.FindFirst("org_id")?.Value;
            logger.LogInformation("JWT Token validated successfully. UserId: {UserId}, OrgId: {OrgId}", userId, orgId);
            return Task.CompletedTask;
        },
        OnChallenge = context =>
        {
            var logger = context.HttpContext.RequestServices.GetRequiredService<ILogger<Program>>();
            logger.LogWarning("JWT Challenge triggered. Error: {Error}, ErrorDescription: {ErrorDescription}", 
                context.Error, context.ErrorDescription);
            return Task.CompletedTask;
        },
        OnMessageReceived = context =>
        {
            // Skip JWT processing for Stripe webhook endpoint (Stripe doesn't send Authorization headers)
            var path = context.HttpContext.Request.Path.Value ?? "";
            if (path.Contains("/webhooks/stripe", StringComparison.OrdinalIgnoreCase))
            {
                context.Token = null; // Prevent JWT validation for webhook endpoint
                return Task.CompletedTask;
            }
            
            var logger = context.HttpContext.RequestServices.GetRequiredService<ILogger<Program>>();
            var token = context.Token;
            
            if (string.IsNullOrEmpty(token))
            {
                logger.LogWarning("No token received in Authorization header");
            }
            else
            {
                // Handle the case where "Bearer" might be duplicated in the header
                if (token.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                {
                    logger.LogInformation("Removing duplicate 'Bearer' prefix from token");
                    token = token.Substring("Bearer ".Length).Trim();
                    context.Token = token;
                }
                
                // Log first few characters of token for debugging (don't log full token for security)
                var tokenPreview = token.Length > 20 ? token.Substring(0, 20) + "..." : token;
                logger.LogInformation("Processing token: {TokenPreview}", tokenPreview);
            }
            
            return Task.CompletedTask;
        }
    };
    
});

builder.Services.AddAuthorization();

// Configure CORS - Frontend communicates through port 5000
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.WithOrigins("http://localhost:5000")
              .AllowAnyMethod()
              .AllowAnyHeader()
              .AllowCredentials();
    });
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseCors();

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();
