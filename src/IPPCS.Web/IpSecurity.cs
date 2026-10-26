using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.RateLimiting;

// Independent local authentication; no HR, AD, or existing identity service integration.
public record IpLoginInput(string Username, string Password);

public static class IpSecuritySetup
{
    public static IServiceCollection AddIpSecurity(this IServiceCollection services)
    {
        services.AddIdentity<IdentityUser,IdentityRole>(options=>{
            options.Password.RequiredLength=12;
            options.Password.RequireDigit=true;
            options.Password.RequireLowercase=true;
            options.Password.RequireUppercase=true;
            options.Password.RequireNonAlphanumeric=true;
            options.Lockout.DefaultLockoutTimeSpan=TimeSpan.FromMinutes(20);
            options.Lockout.MaxFailedAccessAttempts=5;
            options.Lockout.AllowedForNewUsers=true;
            options.User.RequireUniqueEmail=false;
        })
        .AddEntityFrameworkStores<ProductionDb>()
        .AddDefaultTokenProviders();
        services.ConfigureApplicationCookie(options=>{
            options.Cookie.Name="IPPCS.Auth";
            options.Cookie.HttpOnly=true;
            options.Cookie.SameSite=SameSiteMode.Strict;
            options.Cookie.SecurePolicy=CookieSecurePolicy.SameAsRequest; // HTTPS mandatory in production, which is blocked for prototype.
            options.ExpireTimeSpan=TimeSpan.FromMinutes(25);
            options.SlidingExpiration=true;
            options.LoginPath="/login.html";
            options.Events.OnRedirectToLogin=ctx=>{
                if(ctx.Request.Path.StartsWithSegments("/api"))
                {
                    ctx.Response.StatusCode=StatusCodes.Status401Unauthorized;
                    return Task.CompletedTask;
                }
                ctx.Response.Redirect(ctx.RedirectUri);return Task.CompletedTask;
            };
            options.Events.OnRedirectToAccessDenied=ctx=>{
                if(ctx.Request.Path.StartsWithSegments("/api"))
                {
                    ctx.Response.StatusCode=StatusCodes.Status403Forbidden;
                    return Task.CompletedTask;
                }
                ctx.Response.Redirect(ctx.RedirectUri);return Task.CompletedTask;
            };
        });
        services.AddAuthorization(options=>{
            options.AddPolicy("IpProductionUser",p=>p.RequireRole("Administrator","Planner","ProductionOperator","QualityInspector"));
            options.AddPolicy("IpQualityApprove",p=>p.RequireRole("Administrator","QualityInspector"));
        });
        services.AddAntiforgery(options=>{
            options.Cookie.Name="IPPCS.XSRF";
            options.Cookie.HttpOnly=true;
            options.Cookie.SameSite=SameSiteMode.Strict;
            options.HeaderName="X-IPPCS-CSRF";
        });
        services.AddHostedService<IpDevelopmentBootstrap>();
        return services;
    }

    public static IEndpointRouteBuilder MapIpAuth(this IEndpointRouteBuilder routes)
    {
        routes.MapPost("/auth/login",async(IpLoginInput input,SignInManager<IdentityUser> signIn,UserManager<IdentityUser> users)=>{
            if(string.IsNullOrWhiteSpace(input.Username)||string.IsNullOrEmpty(input.Password)
                ||input.Username.Length>120||input.Password.Length>256)
                return Results.BadRequest(new{message="نام کاربری یا رمز عبور نامعتبر است."});
            var user=await users.FindByNameAsync(input.Username.Trim());
            if(user==null)return Results.Unauthorized();
            var result=await signIn.PasswordSignInAsync(user,input.Password,isPersistent:false,lockoutOnFailure:true);
            return result.Succeeded?Results.Ok(new{message="ورود موفق"}):
                result.IsLockedOut?Results.StatusCode(423):Results.Unauthorized();
        }).RequireRateLimiting("ip-login");
        routes.MapGet("/auth/csrf",(HttpContext context,IAntiforgery af)=>{
            var tokens=af.GetAndStoreTokens(context);
            return Results.Ok(new{token=tokens.RequestToken});
        }).RequireAuthorization("IpProductionUser");
        routes.MapGet("/auth/me",(HttpContext ctx)=>Results.Ok(new{username=ctx.User.Identity?.Name}))
            .RequireAuthorization("IpProductionUser");
        routes.MapPost("/api/v1/auth/logout",async(SignInManager<IdentityUser> signIn)=>{
            await signIn.SignOutAsync();return Results.NoContent();
        }).RequireAuthorization("IpProductionUser");
        return routes;
    }

    public static IApplicationBuilder UseIpCsrfProtection(this IApplicationBuilder app)
    {
        return app.Use(async(context,next)=>{
            if(context.Request.Path.StartsWithSegments("/api/v1")
                && (HttpMethods.IsPost(context.Request.Method)||HttpMethods.IsPut(context.Request.Method)
                    ||HttpMethods.IsPatch(context.Request.Method)||HttpMethods.IsDelete(context.Request.Method)))
            {
                try { await context.RequestServices.GetRequiredService<IAntiforgery>().ValidateRequestAsync(context); }
                catch(AntiforgeryValidationException)
                {
                    context.Response.StatusCode=StatusCodes.Status400BadRequest;
                    await context.Response.WriteAsJsonAsync(new{message="نشست یا شناسه امنیت درخواست نامعتبر است. صفحه را دوباره بارگذاری کنید."});
                    return;
                }
            }
            await next(context);
        });
    }
}

// Requires explicitly configured Development credentials. No embedded default password.
public sealed class IpDevelopmentBootstrap(IServiceProvider services,IConfiguration config,
    IWebHostEnvironment env,ILogger<IpDevelopmentBootstrap> log):IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if(!env.IsDevelopment())return;
        var name=config["Bootstrap:AdminUsername"];
        var password=config["Bootstrap:AdminPassword"];
        if(string.IsNullOrWhiteSpace(name)||string.IsNullOrWhiteSpace(password))
        {
            log.LogWarning("IPPCS local bootstrap is disabled until Bootstrap:AdminUsername and Bootstrap:AdminPassword are configured.");
            return;
        }
        using var scope=services.CreateScope();
        var db=scope.ServiceProvider.GetRequiredService<ProductionDb>();
        if(!await db.Database.CanConnectAsync(cancellationToken))
        {
            log.LogWarning("IPPCS development database is not ready. Apply reviewed migrations before admin bootstrap.");
            return;
        }
        var roles=scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        var users=scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
        if(!await roles.RoleExistsAsync("Administrator"))
        {
            var created=await roles.CreateAsync(new IdentityRole("Administrator"));
            if(!created.Succeeded)throw new InvalidOperationException("Administrator role creation failed.");
        }
        var user=await users.FindByNameAsync(name);
        if(user is null)
        {
            user=new IdentityUser{UserName=name,EmailConfirmed=true};
            var created=await users.CreateAsync(user,password);
            if(!created.Succeeded)throw new InvalidOperationException("Bootstrap admin failed password policy.");
        }
        if(!await users.IsInRoleAsync(user,"Administrator"))
        {
            var result=await users.AddToRoleAsync(user,"Administrator");
            if(!result.Succeeded)throw new InvalidOperationException("Adding initial admin role failed.");
        }
    }
    public Task StopAsync(CancellationToken cancellationToken)=>Task.CompletedTask;
}
