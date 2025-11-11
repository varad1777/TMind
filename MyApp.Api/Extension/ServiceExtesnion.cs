using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Security.Claims;
using System.Text;

namespace MyApp.Api.Extension
{
    public static class ServiceExtesnion
    {
        public static IServiceCollection AddCustomAuthentication(this IServiceCollection services, IConfiguration configuration)
        {
            // Adding authentication service to the application
            services.AddAuthentication(options =>
            {
                // Agar system ko kahin clear nahi bataya gaya ki kaunsa authentication use karna hai,
                // to Cookie Authentication default scheme hogi.
                options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;

                // Jab user ko verify (authenticate) karna ho, to JWT Token se verify karega.
                options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;

            })
            // Cookie Authentication configuration
            .AddCookie(options =>
            {
                // Cookies sirf HTTPS ke through hi aayengi
                options.Cookie.SecurePolicy = CookieSecurePolicy.Always;

                // Cross-site request ke liye bhi cookies send karne ke liye (for frontend-backend communication)
                options.Cookie.SameSite = SameSiteMode.None;

                // Cookie 15 minutes ke liye valid rahegi
                options.ExpireTimeSpan = TimeSpan.FromMinutes(15);
            })
            // JWT Authentication configuration
            .AddJwtBearer(options =>
            {
                // Token validation parameters specify karte hai ki token kaise verify hoga
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    // Issuer (token dene wala) verify kare
                    ValidateIssuer = true,

                    // Audience (token lene wala) verify kare
                    ValidateAudience = true,

                    // Token expiry time verify kare
                    ValidateLifetime = true,

                    // Token ke signing key ko verify kare
                    ValidateIssuerSigningKey = true,

                    // Valid issuer value appsettings.json me define ki gayi hai
                    ValidIssuer = configuration["Jwt:Issuer"],

                    // Valid audience value appsettings.json me define ki gayi hai
                    ValidAudience = configuration["Jwt:Audience"],

                    // Token ko verify karne ke liye symmetric security key use hoti hai
                    IssuerSigningKey = new SymmetricSecurityKey(
                        Encoding.UTF8.GetBytes(configuration["Jwt:Key"])),

                    // Token ke andar role claim ko identify kare
                    RoleClaimType = ClaimTypes.Role,

                    // ClockSkew zero karne se token exact expiry time par expire hoga
                    ClockSkew = TimeSpan.Zero
                };

                // JWT events handle karne ke liye (custom logic ke liye)
                options.Events = new JwtBearerEvents
                {
                    // Ye event token ko request ke cookies se read karta hai
                    OnMessageReceived = context =>
                    {
                        var token = context.Request.Cookies["access_token"];
                        if (!string.IsNullOrEmpty(token))
                            context.Token = token;
                        return Task.CompletedTask;
                    },
                    // Agar authentication fail hoti hai to error log karta hai
                    OnAuthenticationFailed = context =>
                    {
                        var logger = context.HttpContext.RequestServices.GetRequiredService<ILogger<Program>>();
                        logger.LogError(context.Exception, "JWT Authentication failed");
                        return Task.CompletedTask;
                    }
                };
            });

            // Authorization policies define karna
            services.AddAuthorization(options =>
            {
                // Sirf "Admin" role wale users ke liye access allow karega
                options.AddPolicy("AdminOnly", policy => policy.RequireRole("Admin"));

                // "Manager" ya "Admin" dono roles ke users ke liye access allow karega
                options.AddPolicy("ManagerOrAdmin", policy => policy.RequireRole("Manager", "Admin"));
            });

            return services;
        }
    }
}
