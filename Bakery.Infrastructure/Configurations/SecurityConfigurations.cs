using Bakery.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bakery.Infrastructure.Configurations;

public sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("Users");
        builder.ConfigureBaseEntity();
        builder.Property(user => user.Username).HasMaxLength(100).IsRequired();
        builder.Property(user => user.NormalizedUsername).HasMaxLength(100).IsRequired();
        builder.Property(user => user.FullName).HasMaxLength(150).IsRequired();
        builder.Property(user => user.PasswordHash).HasMaxLength(500).IsRequired();
        builder.Property(user => user.SecurityStamp).HasMaxLength(32).IsRequired();
        builder.Property(user => user.IsSuperAdmin).HasDefaultValue(false);
        builder.HasIndex(user => user.NormalizedUsername).IsUnique().HasFilter("[IsDeleted] = 0");
    }
}

public sealed class PermissionConfiguration : IEntityTypeConfiguration<Permission>
{
    public void Configure(EntityTypeBuilder<Permission> builder)
    {
        builder.ToTable("Permissions");
        builder.ConfigureBaseEntity();
        builder.Property(permission => permission.Key).HasMaxLength(150).IsRequired();
        builder.Property(permission => permission.DisplayName).HasMaxLength(150).IsRequired();
        builder.Property(permission => permission.Category).HasMaxLength(100).IsRequired();
        builder.HasIndex(permission => permission.Key).IsUnique().HasFilter("[IsDeleted] = 0");
    }
}

public sealed class UserPermissionConfiguration : IEntityTypeConfiguration<UserPermission>
{
    public void Configure(EntityTypeBuilder<UserPermission> builder)
    {
        builder.ToTable("UserPermissions");
        builder.HasQueryFilter(userPermission =>
            !userPermission.User.IsDeleted && !userPermission.Permission.IsDeleted);
        builder.HasKey(userPermission => new { userPermission.UserId, userPermission.PermissionId });
        builder.HasOne(userPermission => userPermission.User)
            .WithMany(user => user.UserPermissions)
            .HasForeignKey(userPermission => userPermission.UserId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(userPermission => userPermission.Permission)
            .WithMany(permission => permission.UserPermissions)
            .HasForeignKey(userPermission => userPermission.PermissionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class RoleConfiguration : IEntityTypeConfiguration<Role>
{
    public void Configure(EntityTypeBuilder<Role> builder)
    {
        builder.ToTable("Roles");
        builder.ConfigureBaseEntity();
        builder.Property(role => role.Name).HasMaxLength(120).IsRequired();
        builder.Property(role => role.NormalizedName).HasMaxLength(120).IsRequired();
        builder.Property(role => role.Description).HasMaxLength(500);
        builder.HasIndex(role => role.NormalizedName).IsUnique().HasFilter("[IsDeleted] = 0");
    }
}

public sealed class RolePermissionConfiguration : IEntityTypeConfiguration<RolePermission>
{
    public void Configure(EntityTypeBuilder<RolePermission> builder)
    {
        builder.ToTable("RolePermissions");
        builder.HasQueryFilter(item => !item.Role.IsDeleted && !item.Permission.IsDeleted);
        builder.HasKey(item => new { item.RoleId, item.PermissionId });
        builder.HasOne(item => item.Role).WithMany(role => role.RolePermissions)
            .HasForeignKey(item => item.RoleId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(item => item.Permission).WithMany()
            .HasForeignKey(item => item.PermissionId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class UserRoleConfiguration : IEntityTypeConfiguration<UserRole>
{
    public void Configure(EntityTypeBuilder<UserRole> builder)
    {
        builder.ToTable("UserRoles");
        builder.HasQueryFilter(item => !item.User.IsDeleted && !item.Role.IsDeleted);
        builder.HasKey(item => new { item.UserId, item.RoleId });
        builder.HasOne(item => item.User).WithMany(user => user.UserRoles)
            .HasForeignKey(item => item.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(item => item.Role).WithMany(role => role.UserRoles)
            .HasForeignKey(item => item.RoleId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class UserSafePermissionConfiguration : IEntityTypeConfiguration<UserSafePermission>
{
    public void Configure(EntityTypeBuilder<UserSafePermission> builder)
    {
        builder.ToTable("UserSafePermissions");
        builder.ConfigureBaseEntity();
        builder.HasOne(x => x.Branch).WithMany().HasForeignKey(x => x.BranchId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.UserId, x.SafeId }).IsUnique().HasFilter("[IsDeleted] = 0");
        builder.HasOne(x => x.User)
            .WithMany(user => user.UserSafePermissions)
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.Safe)
            .WithMany()
            .HasForeignKey(x => x.SafeId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class BranchConfiguration : IEntityTypeConfiguration<Branch>
{
    public void Configure(EntityTypeBuilder<Branch> builder)
    {
        builder.ToTable("Branches");
        builder.ConfigureBaseEntity();
        builder.Property(b => b.Code).HasMaxLength(50).IsRequired();
        builder.Property(b => b.Name).HasMaxLength(150).IsRequired();
        builder.HasIndex(b => b.Code).IsUnique().HasFilter("[IsDeleted] = 0");
    }
}

public sealed class UserBranchConfiguration : IEntityTypeConfiguration<UserBranch>
{
    public void Configure(EntityTypeBuilder<UserBranch> builder)
    {
        builder.ToTable("UserBranches");
        builder.HasQueryFilter(userBranch =>
            !userBranch.User.IsDeleted && !userBranch.Branch.IsDeleted);
        builder.HasKey(ub => new { ub.UserId, ub.BranchId });
        builder.HasOne(ub => ub.User)
            .WithMany(u => u.UserBranches)
            .HasForeignKey(ub => ub.UserId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(ub => ub.Branch)
            .WithMany(b => b.UserBranches)
            .HasForeignKey(ub => ub.BranchId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
