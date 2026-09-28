using Microsoft.EntityFrameworkCore;
using NovaChat.Server.Data;

namespace NovaChat.Server.Services;

public class AdminService
{
    private readonly AppDbContext _db;
    public AdminService(AppDbContext db) => _db = db;
    public Task<int> GetUserCountAsync() => _db.Users.CountAsync();
    public Task<bool> UserExistsAsync(string id) => long.TryParse(id, out var userId) && userId > 0 ? _db.Users.AnyAsync(u => u.Id == userId) : Task.FromResult(false);
}
