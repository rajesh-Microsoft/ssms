using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SMMS.Api.Data;
using SMMS.Api.Models;
using SMMS.Api.Services;
using Xunit;

namespace SMMS.Api.Tests;

/// <summary>
/// Covers how a user's effective permissions are worked out once positions become groups.
/// The overriding requirement is that nobody loses access they had before the change.
/// </summary>
public class UserGroupPermissionTests : IDisposable
{
    private readonly List<SqliteConnection> _connections = [];

    private SmmsDbContext NewSociety()
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        _connections.Add(connection);
        var options = new DbContextOptionsBuilder<SmmsDbContext>().UseSqlite(connection).Options;
        var db = new SmmsDbContext(options);
        db.Database.EnsureCreated();
        return db;
    }

    private static User AddUser(SmmsDbContext db, string username, string? permissions = null, string status = "Active", string role = "Member")
    {
        var user = new User { Username = username, PasswordHash = "x", Role = role, Status = status, Permissions = permissions };
        db.Users.Add(user);
        db.SaveChanges();
        return user;
    }

    private static UserGroup AddGroup(SmmsDbContext db, string name, Dictionary<string, string> permissions)
    {
        var group = new UserGroup { Name = name, IsActive = true, Permissions = PermissionHelper.Serialize(permissions) };
        db.UserGroups.Add(group);
        db.SaveChanges();
        return group;
    }

    private static void Join(SmmsDbContext db, UserGroup g, User u)
    {
        db.UserGroupMembers.Add(new UserGroupMember { UserGroupId = g.Id, UserId = u.Id, AddedOn = DateTime.UtcNow });
        db.SaveChanges();
    }

    public void Dispose()
    {
        foreach (var c in _connections) c.Dispose();
        GC.SuppressFinalize(this);
    }

    // ── The migration guarantee ───────────────────────────────────────────────────────────

    /// <summary>The whole rollout depends on this: an account that has not been touched keeps
    /// exactly the permissions it had, whether or not groups exist.</summary>
    [Fact]
    public async Task ExistingUserWithIndividualPermissions_KeepsThemExactly()
    {
        using var db = NewSociety();
        var stored = PermissionHelper.Serialize(new()
        {
            [PermissionModules.Expenses] = "Edit",
            [PermissionModules.Settings] = "None"
        });
        var user = AddUser(db, "legacy", stored);
        AddGroup(db, "Treasurer", new() { [PermissionModules.Collections] = "Edit" });

        var access = await new EffectivePermissionService(db).ResolveAsync(user.Id);

        Assert.NotNull(access);
        Assert.True(access!.UsesIndividualOverride);
        Assert.Equal("Edit", access.Permissions[PermissionModules.Expenses]);
        Assert.Equal("None", access.Permissions[PermissionModules.Settings]);
        Assert.Equal("View", access.Permissions[PermissionModules.Collections]);
    }

    /// <summary>An override wins outright. Merging would silently widen access beyond what the
    /// user's own screen shows.</summary>
    [Fact]
    public async Task IndividualOverride_IgnoresGroupsEvenWhenMoreGenerous()
    {
        using var db = NewSociety();
        var user = AddUser(db, "u", PermissionHelper.Serialize(new() { [PermissionModules.Expenses] = "None" }));
        var treasurer = AddGroup(db, "Treasurer", new() { [PermissionModules.Expenses] = "Edit" });
        Join(db, treasurer, user);

        var access = await new EffectivePermissionService(db).ResolveAsync(user.Id);

        Assert.Equal("None", access!.Permissions[PermissionModules.Expenses]);
        Assert.True(access.UsesIndividualOverride);
    }

    /// <summary>A member in no group must keep the platform's read-only default, or every
    /// resident would lose access the moment groups shipped.</summary>
    [Fact]
    public async Task UserWithNoOverrideAndNoGroups_GetsTheViewDefault()
    {
        using var db = NewSociety();
        var user = AddUser(db, "plain");

        var access = await new EffectivePermissionService(db).ResolveAsync(user.Id);

        Assert.False(access!.UsesIndividualOverride);
        Assert.All(PermissionModules.All, m => Assert.Equal("View", access.Permissions[m]));
    }

    // ── Group inheritance ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GroupMembership_GrantsTheGroupsPermissions()
    {
        using var db = NewSociety();
        var user = AddUser(db, "rajesh");
        var treasurer = AddGroup(db, "Treasurer", new()
        {
            [PermissionModules.Expenses] = "Edit",
            [PermissionModules.Liabilities] = "Edit",
            [PermissionModules.Settings] = "None"
        });
        Join(db, treasurer, user);

        var access = await new EffectivePermissionService(db).ResolveAsync(user.Id);

        Assert.Equal("Edit", access!.Permissions[PermissionModules.Expenses]);
        Assert.Equal("Edit", access.Permissions[PermissionModules.Liabilities]);
        Assert.Equal("None", access.Permissions[PermissionModules.Settings]);
        Assert.Equal("View", access.Permissions[PermissionModules.Members]);
    }

    [Fact]
    public async Task TwoGroups_MostPermissiveWins()
    {
        using var db = NewSociety();
        var user = AddUser(db, "suresh");
        var treasurer = AddGroup(db, "Treasurer", new() { [PermissionModules.Expenses] = "Edit" });
        var festival = AddGroup(db, "Festival", new() { [PermissionModules.Expenses] = "View" });
        Join(db, treasurer, user);
        Join(db, festival, user);

        var access = await new EffectivePermissionService(db).ResolveAsync(user.Id);

        Assert.Equal("Edit", access!.Permissions[PermissionModules.Expenses]);
    }

    /// <summary>A module every one of the user's groups denies must end up denied, otherwise a
    /// group could never take away the "View" baseline.</summary>
    [Fact]
    public async Task ModuleDeniedByEveryGroup_ResolvesToNone()
    {
        using var db = NewSociety();
        var user = AddUser(db, "u");
        var a = AddGroup(db, "A", new() { [PermissionModules.Settings] = "None" });
        var b = AddGroup(db, "B", new() { [PermissionModules.Settings] = "None" });
        Join(db, a, user);
        Join(db, b, user);

        var access = await new EffectivePermissionService(db).ResolveAsync(user.Id);

        Assert.Equal("None", access!.Permissions[PermissionModules.Settings]);
    }

    [Fact]
    public async Task OneGroupAllowing_BeatsAnotherDenying()
    {
        using var db = NewSociety();
        var user = AddUser(db, "u");
        var denies = AddGroup(db, "Denies", new() { [PermissionModules.Budgets] = "None" });
        var allows = AddGroup(db, "Allows", new() { [PermissionModules.Budgets] = "Edit" });
        Join(db, denies, user);
        Join(db, allows, user);

        var access = await new EffectivePermissionService(db).ResolveAsync(user.Id);

        Assert.Equal("Edit", access!.Permissions[PermissionModules.Budgets]);
    }

    [Fact]
    public async Task InactiveGroup_GrantsNothing()
    {
        using var db = NewSociety();
        var user = AddUser(db, "u");
        var group = AddGroup(db, "Retired", new() { [PermissionModules.Expenses] = "Edit" });
        Join(db, group, user);
        group.IsActive = false;
        db.SaveChanges();

        var access = await new EffectivePermissionService(db).ResolveAsync(user.Id);

        Assert.Equal("View", access!.Permissions[PermissionModules.Expenses]);
    }

    // ── Revocation happens immediately ────────────────────────────────────────────────────

    /// <summary>The reason permissions are resolved per request rather than read from the token:
    /// removing someone from a group has to take effect now, not in up to two hours.</summary>
    [Fact]
    public async Task RemovingFromGroup_RevokesImmediately()
    {
        using var db = NewSociety();
        var resolver = new EffectivePermissionService(db);
        var user = AddUser(db, "treasurer");
        var group = AddGroup(db, "Treasurer", new() { [PermissionModules.Expenses] = "Edit" });
        Join(db, group, user);

        Assert.Equal("Edit", (await resolver.ResolveAsync(user.Id))!.Permissions[PermissionModules.Expenses]);

        db.UserGroupMembers.Remove(db.UserGroupMembers.Single(m => m.UserId == user.Id));
        db.SaveChanges();

        Assert.Equal("View", (await resolver.ResolveAsync(user.Id))!.Permissions[PermissionModules.Expenses]);
    }

    [Fact]
    public async Task EditingGroupPermissions_AffectsEveryMemberAtOnce()
    {
        using var db = NewSociety();
        var resolver = new EffectivePermissionService(db);
        var group = AddGroup(db, "Treasurer", new() { [PermissionModules.Expenses] = "Edit" });
        var members = new[] { AddUser(db, "a"), AddUser(db, "b"), AddUser(db, "c") };
        foreach (var m in members) Join(db, group, m);

        foreach (var m in members)
            Assert.Equal("Edit", (await resolver.ResolveAsync(m.Id))!.Permissions[PermissionModules.Expenses]);

        group.Permissions = PermissionHelper.Serialize(new() { [PermissionModules.Expenses] = "View" });
        db.SaveChanges();

        foreach (var m in members)
            Assert.Equal("View", (await resolver.ResolveAsync(m.Id))!.Permissions[PermissionModules.Expenses]);
    }

    // ── Account state ─────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("Inactive")]
    [InlineData("Pending")]
    public async Task NonActiveAccount_ResolvesToNothing(string status)
    {
        using var db = NewSociety();
        var user = AddUser(db, "gone", status: status);

        Assert.Null(await new EffectivePermissionService(db).ResolveAsync(user.Id));
    }

    [Fact]
    public async Task DeletedAccount_ResolvesToNothing()
    {
        using var db = NewSociety();
        Assert.Null(await new EffectivePermissionService(db).ResolveAsync(4242));
    }

    /// <summary>Admins bypass module permissions entirely, so their access must not depend on
    /// anyone remembering to put them in a group.</summary>
    [Fact]
    public async Task AdminInNoGroup_StillResolvesAsAdmin()
    {
        using var db = NewSociety();
        var admin = AddUser(db, "admin", role: Roles.Admin);

        var access = await new EffectivePermissionService(db).ResolveAsync(admin.Id);

        Assert.Equal(Roles.Admin, access!.Role);
    }

    // ── Combine() in isolation ────────────────────────────────────────────────────────────

    [Fact]
    public void Combine_WithNoGroups_IsTheViewDefault()
    {
        var combined = PermissionHelper.Combine([]);
        Assert.All(PermissionModules.All, m => Assert.Equal("View", combined[m]));
    }

    [Fact]
    public void Combine_CoversEveryModuleIncludingNewOnes()
    {
        var combined = PermissionHelper.Combine([PermissionHelper.Serialize(new() { [PermissionModules.Expenses] = "Edit" })]);
        Assert.Equal(PermissionModules.All.Length, combined.Count);
        Assert.Contains(PermissionModules.Inventory, combined.Keys);
    }

    // ── Controller queries must survive translation to SQL ────────────────────────────────

    /// <summary>The group detail screen reads members with a name-or-username sort. Doing that in
    /// the query threw at runtime because the fallback cannot be translated, which the resolver
    /// tests could not catch — the list must be materialised before it is sorted.</summary>
    [Fact]
    public async Task GroupMembers_AreReadableAndSortedByDisplayName()
    {
        using var db = NewSociety();
        var group = AddGroup(db, "Treasurer", new() { [PermissionModules.Expenses] = "Edit" });
        var withName = AddUser(db, "zzz-username");
        withName.Name = "Aaron";
        var noName = AddUser(db, "bbb-username");
        db.SaveChanges();
        Join(db, group, withName);
        Join(db, group, noName);

        var members = await db.UserGroupMembers.AsNoTracking()
            .Where(m => m.UserGroupId == group.Id)
            .Select(m => new { m.UserId, m.User!.Username, m.User.Name })
            .ToListAsync();
        var ordered = members.OrderBy(m => m.Name ?? m.Username, StringComparer.OrdinalIgnoreCase).ToList();

        Assert.Equal(2, ordered.Count);
        Assert.Equal("Aaron", ordered[0].Name);
        Assert.Equal("bbb-username", ordered[1].Username);
    }
}
