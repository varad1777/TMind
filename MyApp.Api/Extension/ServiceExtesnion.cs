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
                // Default authentication scheme
                options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;

                // Default authenticate scheme (JWT)
                options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
            })
            // ✅ Cookie Authentication
            .AddCookie(options =>
            {
                options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
                options.Cookie.SameSite = SameSiteMode.None;
                options.ExpireTimeSpan = TimeSpan.FromMinutes(15);
            })
            // ✅ JWT Authentication
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
                    ClockSkew = TimeSpan.Zero
                };

                options.Events = new JwtBearerEvents
                {
                    // 🔸 Extract token from cookie safely
                    OnMessageReceived = async context =>
                    {
                        var token = context.Request.Cookies["access_token"];

                        if (!string.IsNullOrEmpty(token))
                        {
                            // ✅ Basic structure validation (must be header.payload.signature)
                            if (token.Count(c => c == '.') == 2)
                            {
                                context.Token = token;
                            }
                            else
                            {
                                context.NoResult();
                                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                                context.Response.ContentType = "application/json";

                                var result = System.Text.Json.JsonSerializer.Serialize(new
                                {
                                    status = 401,
                                    message = "Access Denied: Invalid token format in cookie"
                                });

                                await context.Response.WriteAsync(result);
                            }
                        }
                    },

                    // 🔸 Handles expired, tampered, or invalid signature tokens
                    OnAuthenticationFailed = context =>
                    {
                        var logger = context.HttpContext.RequestServices.GetRequiredService<ILogger<Program>>();
                        logger.LogError(context.Exception, "JWT Authentication failed");

                        var response = context.Response;
                        var request = context.HttpContext.Request;

                        // ❌ Remove bad cookie
                        if (request.Cookies.ContainsKey("access_token"))
                        {
                            response.Cookies.Delete("access_token", new CookieOptions
                            {
                                HttpOnly = true,
                                Secure = true,
                                SameSite = SameSiteMode.None
                            });
                        }

                        response.StatusCode = StatusCodes.Status401Unauthorized;
                        response.ContentType = "application/json";

                        string message = context.Exception switch
                        {
                            SecurityTokenExpiredException => "Access Denied: Token expired",
                            SecurityTokenInvalidSignatureException => "Access Denied: Invalid token signature (tampered)",
                            SecurityTokenInvalidAudienceException => "Access Denied: Invalid audience",
                            SecurityTokenInvalidIssuerException => "Access Denied: Invalid issuer",
                            ArgumentException => "Access Denied: Malformed token",
                            _ => "Access Denied: Invalid or tampered token"
                        };

                        var result = System.Text.Json.JsonSerializer.Serialize(new
                        {
                            status = 401,
                            message
                        });

                        return response.WriteAsync(result);
                    },

                    // 🔸 Handles missing token or failed challenge
                    OnChallenge = context =>
                    {
                        if (!context.Handled)
                        {
                            context.HandleResponse();
                            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                            context.Response.ContentType = "application/json";
                            var result = System.Text.Json.JsonSerializer.Serialize(new
                            {
                                message = "Access Denied: Token missing or invalid",
                                status = 401
                            });
                            return context.Response.WriteAsync(result);
                        }
                        return Task.CompletedTask;
                    },

                    // Handles forbidden (role-based restriction)
                    OnForbidden = context =>
                    {
                        context.Response.StatusCode = 403;
                        context.Response.ContentType = "application/json";

                        var result = System.Text.Json.JsonSerializer.Serialize(new
                        {
                            status = 403,
                            message = "Access Denied: You do not have permission to access this resource."
                        });

                        return context.Response.WriteAsync(result);
                    }

                };
            });

            // ✅ Define Authorization Policies
            services.AddAuthorization(options =>
            {
                options.AddPolicy("AdminOnly", policy => policy.RequireRole("Admin"));
                options.AddPolicy("ManagerOrAdmin", policy => policy.RequireRole("Manager", "Admin"));
            });

            return services;
        }
    }
};