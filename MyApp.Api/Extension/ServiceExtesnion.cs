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
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ValidIssuer = configuration["Jwt:Issuer"],
                    ValidAudience = configuration["Jwt:Audience"],
                    IssuerSigningKey = new SymmetricSecurityKey(
                        Encoding.UTF8.GetBytes(configuration["Jwt:Key"])),
                    RoleClaimType = ClaimTypes.Role,
                    ClockSkew = TimeSpan.Zero // Ensures exact expiry time
                };

                options.Events = new JwtBearerEvents
                {
                    OnMessageReceived = context =>
                    {
                        // Read token from cookie
                        var token = context.Request.Cookies["access_token"];
                        if (!string.IsNullOrEmpty(token))
                            context.Token = token;
                        return Task.CompletedTask;
                    },

                    OnAuthenticationFailed = context =>
                    {
                        var logger = context.HttpContext.RequestServices.GetRequiredService<ILogger<Program>>();
                        logger.LogError(context.Exception, "JWT Authentication failed");

                        if (context.Exception is SecurityTokenExpiredException)
                        {
                            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                            context.Response.ContentType = "application/json";
                            var result = System.Text.Json.JsonSerializer.Serialize(new
                            {
                                message = "Access Denied: Token Expired",
                                status = 401
                            });
                            return context.Response.WriteAsync(result);
                        }

                        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                        context.Response.ContentType = "application/json";
                        var genericError = System.Text.Json.JsonSerializer.Serialize(new
                        {
                            message = "Access Denied: Invalid Token",
                            status = 401
                        });
                        return context.Response.WriteAsync(genericError);
                    },

                    OnChallenge = context =>
                    {
                        // If token is missing or invalid
                        if (!context.Handled)
                        {
                            context.HandleResponse();
                            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                            context.Response.ContentType = "application/json";
                            var result = System.Text.Json.JsonSerializer.Serialize(new
                            {
                                message = "Access Denied: Token Missing or Invalid",
                                status = 401
                            });
                            return context.Response.WriteAsync(result);
                        }
                        return Task.CompletedTask;
                    }

                     OnForbidden = context =>
                     {
                         // 🔸 User authenticated but not authorized (role mismatch)
                         context.Response.StatusCode = 402; // custom code instead of 403
                         context.Response.ContentType = "application/json";

                         var result = System.Text.Json.JsonSerializer.Serialize(new
                         {
                             status = 402,
                             message = "Access Denied: You do not have permission to access this resource."
                         });

                         return context.Response.WriteAsync(result);
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
