using Microsoft.EntityFrameworkCore;

namespace NewApi.Api.Data;

public class TodoItem
{
    public int Id { get; set; }
    public required string Title { get; set; }
    public bool IsDone { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
}

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<TodoItem> Todos => Set<TodoItem>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<TodoItem>().Property(t => t.Title).HasMaxLength(200);
    }
}