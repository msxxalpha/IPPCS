using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

// Application accounts only. No employee profiles, HR integration or directory sync.
public record IpLocalAccountInput(string Username,string Password,string Role);
public record IpChangePasswordInput(string CurrentPassword,string NewPassword);

public static class IpLocalAccountsEndpoints
{
    private static readonly string[] AllowedAssignableRoles=
        ["Planner","ProductionOperator","QualityInspector"];

    public static IEndpointRouteBuilder MapLocalAccounts(this IEndpointRouteBuilder app)
    {
        var api=app.MapGroup("/api/v1");
        api.MapGet("/system-users",async(UserManager<IdentityUser> users,CancellationToken ct)=>{
            var accounts=await users.Users.AsNoTracking().OrderBy(x=>x.UserName).Take(300).ToListAsync(ct);
            var rows=new List<object>();
            foreach(var account in accounts){
                var roles=await users.GetRolesAsync(account);
                rows.Add(new{account.Id,account.UserName,
                    roles=roles.OrderBy(x=>x).ToArray(),
                    isLocked=account.LockoutEnd.HasValue&&account.LockoutEnd>DateTimeOffset.UtcNow});
            }
            return Results.Ok(rows);
        }).RequireAuthorization(p=>p.RequireRole("Administrator"));
        api.MapPost("/system-users",async(IpLocalAccountInput input,
            UserManager<IdentityUser> users,RoleManager<IdentityRole> roles)=>{
            if(string.IsNullOrWhiteSpace(input.Username)||input.Username.Trim().Length>100
                ||string.IsNullOrWhiteSpace(input.Password)||input.Password.Length>256
                ||!AllowedAssignableRoles.Contains(input.Role,StringComparer.Ordinal))
                return Results.BadRequest(new{message="نام کاربری، رمز و نقش نرم‌افزار معتبر نیست."});
            var username=input.Username.Trim();
            if(await users.FindByNameAsync(username)!=null)
                return Results.Conflict(new{message="نام کاربری قبلاً ثبت شده است."});
            if(!await roles.RoleExistsAsync(input.Role)){
                var role=await roles.CreateAsync(new IdentityRole(input.Role));
                if(!role.Succeeded)return Results.Problem("ایجاد نقش سیستمی امکان‌پذیر نیست.");
            }
            var user=new IdentityUser{UserName=username,LockoutEnabled=true};
            var created=await users.CreateAsync(user,input.Password);
            if(!created.Succeeded)
                return Results.BadRequest(new{message=string.Join("؛ ",
                    created.Errors.Select(x=>x.Description))});
            var assigned=await users.AddToRoleAsync(user,input.Role);
            if(!assigned.Succeeded){
                await users.DeleteAsync(user);
                return Results.Problem("تخصیص نقش کاربر انجام نشد و حساب بازگردانده شد.");
            }
            return Results.Created($"/api/v1/system-users/{user.Id}",
                new{user.Id,user.UserName,role=input.Role});
        }).RequireAuthorization(p=>p.RequireRole("Administrator"));

        api.MapPost("/system-users/{id}/lock",async(string id,HttpContext ctx,
            UserManager<IdentityUser> users)=>{
            var account=await users.FindByIdAsync(id);
            if(account is null)return Results.NotFound();
            var own=users.GetUserId(ctx.User);
            if(account.Id==own)return Results.Conflict(new{message="مدیر نمی‌تواند حساب فعال خود را قفل کند."});
            if(await users.IsInRoleAsync(account,"Administrator"))
                return Results.Conflict(new{message="قفل‌کردن حساب مدیر از این فرم مجاز نیست."});
            var allowed=await users.SetLockoutEnabledAsync(account,true);
            var result=await users.SetLockoutEndDateAsync(account,DateTimeOffset.MaxValue);
            if(!allowed.Succeeded||!result.Succeeded)
                return Results.Problem("قفل حساب ذخیره نشد.");
            return Results.NoContent();
        }).RequireAuthorization(p=>p.RequireRole("Administrator"));

        api.MapPost("/system-users/{id}/unlock",async(string id,UserManager<IdentityUser> users)=>{
            var account=await users.FindByIdAsync(id);
            if(account is null)return Results.NotFound();
            var unlock=await users.SetLockoutEndDateAsync(account,null);
            var reset=await users.ResetAccessFailedCountAsync(account);
            if(!unlock.Succeeded||!reset.Succeeded)return Results.Problem("فعال‌سازی حساب انجام نشد.");
            return Results.NoContent();
        }).RequireAuthorization(p=>p.RequireRole("Administrator"));

        api.MapPost("/auth/change-password",async(IpChangePasswordInput input,
            HttpContext context,UserManager<IdentityUser> users,SignInManager<IdentityUser> signIn)=>{
            if(string.IsNullOrWhiteSpace(input.CurrentPassword)
                ||string.IsNullOrWhiteSpace(input.NewPassword)
                ||input.NewPassword.Length>256)
                return Results.BadRequest(new{message="رمز فعلی و رمز جدید معتبر لازم است."});
            var account=await users.GetUserAsync(context.User);
            if(account is null)return Results.Unauthorized();
            var changed=await users.ChangePasswordAsync(account,input.CurrentPassword,input.NewPassword);
            if(!changed.Succeeded)
                return Results.BadRequest(new{message=string.Join("؛ ",
                    changed.Errors.Select(x=>x.Description))});
            await signIn.RefreshSignInAsync(account);
            return Results.Ok(new{message="رمز ورود با موفقیت تغییر کرد."});
        }).RequireAuthorization("IpProductionUser");
        return app;
    }
}
